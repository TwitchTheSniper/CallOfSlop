namespace CallOfSlop.MapKit.Generators;

/// <summary>
/// Maps/Zombies/02-AshgraveCathedral.json: "Ashgrave Cathedral", the Zombies pack's second map (developer-approved board
/// item dev-zombies-cathedral-zc01, 2026-10-27; docs/zombies/ZOMBIES-MODE.md, "Ashgrave Cathedral"). An original gothic
/// cathedral at night inside its walled precinct, on six levels:
/// <list type="bullet">
/// <item>Grounds (y 0): the Close (the forecourt and yew walk), the Cloister, the Graveyard, and the outbuildings: the
/// Groundskeeper's Lodge, the Chapter House, the Tithe Barn, the Mausoleum. Outside the precinct wall, the Paupers'
/// Field, where the horde comes from (every window and boarded gate opens onto it).</item>
/// <item>Ground floor (y 0): the Nave with its aisles (the start), the Transept and crossing, the Choir, the Lady
/// Chapel and the Vestry.</item>
/// <item>Upper level: the Organ Loft and the triforium galleries (y 6), the Roof Walk on the north aisle's roof (y 10.8).</item>
/// <item>The Bell Tower (north-west): stairs round its walls to the ringing chamber (y 10.8) and the belfry (y 18).</item>
/// <item>Crypt and Ossuary (y -5) under the choir and the crossing; Dungeons, the Inquisitor's Chamber and the
/// Smugglers' Tunnel (y -10), the tunnel coming up into the Mausoleum.</item>
/// </list>
/// The bell (the belfry): a survival challenge, not a purchase. Ringing it (map logic: a button, a zclimb face and a
/// challenge) floods the horde outside up the tower's north face in a living pyramid (Source/Game/ZombiesClimb.cs) for
/// 75 s; holding the belfry pays points, parts and a Max Ammo (the rules' "climb" section).
/// Zombies areas have heights here (floors over each other; MapZombies.cs "y" and "more"). The map has no Ground op:
/// the surface is a tiled slab with the stairwells cut out, so the crypt and the dungeons can lie under it.
/// The generator checks its own layout (machines, wall buys and keep-clear spots) and throws on a clash.
/// </summary>
public sealed class AshgraveCathedral : IMapGenerator
{
    public string Name => "Ashgrave Cathedral";
    public string Pack => "Zombies";
    public int Number => 2;

    // ---- Levels and sizes ---------------------------------------------------------------------------------------
    // The generator works with the ground at 0 and lifts the whole map by O when it is done (Lift): non-terrain maps keep
    // players above y -5 (World.ClampToArena) and count anyone under the ground (0) as fallen, so the dungeons (-10 here)
    // must end up above 0. The written map has the grounds at y 12, the crypt at 7, the dungeons at 2.
    public const double O = 12;
    const double G = 0, Gal = 6, Roof = 10.8, Belfry = 18, Crypt = -5, Dun = -10, Tun = -6.4; // (Tun: the smugglers' tunnel's raised east end)
    const double T = 0.6; // cathedral walls
    const double Sill = 0.9, WinTop = 2.3, WinW = 1.6;

    // ---- Colours ------------------------------------------------------------------------------------------------
    const string Grass = "#2f3a2a", Earth = "#3a332a", Gravel = "#55524c", Flag = "#57534d", FlagDark = "#45423d", CryptFloor = "#3b3833", DunFloor = "#34312d",
        StoneW = "#7d776c", StoneD = "#5e5950", StoneL = "#958e80", Precinct = "#4a4740", Timber = "#4a3420", BarnW = "#5a4a38", Slate = "#2e3034", Lead = "#3d4148",
        Under = "#3e3a34", UnderD = "#2f2c28";

    Kit k = null!;
    readonly List<(string Name, List<double[]> Boxes)> _areas = new();
    readonly JArray _doors = new(), _windows = new(), _spawners = new(), _wallBuys = new(), _box = new(), _benches = new(), _perks = new(), _slots = new(), _casino = new(), _salvage = new();
    JObject? _power, _slotUp;

    // Layout checks: keep-clear spots (x, y, z, radius, what), machine footprints, wall buys, wall holes.
    readonly List<(double X, double Y, double Z, double R, string What)> _keep = new();
    readonly List<(double X0, double Z0, double X1, double Z1, double Y, string What)> _machines = new();
    readonly List<(double X0, double Z0, double X1, double Z1, double Y, string What)> _holes = new();
    readonly List<(double X0, double Z0, double X1, double Z1, double Y, string What)> _solids = new();
    readonly List<(double X, double Y, double Z, double Yaw, string Build)> _wallBuyList = new();
    readonly List<string> _problems = new();

    public void Build(Kit kit)
    {
        k = kit;
        k.Describe("Zombies: a gothic cathedral and its precinct at night, on six levels: the grounds and their outbuildings, the nave, transept, choir "
                   + "and chapels, the organ loft and roof walk, the bell tower, the crypt and ossuary, and the dungeons with a smugglers' tunnel. Twenty areas, "
                   + "every perk, a slot machine for every part category, four casino tables, the box in twelve spots. Ring the bell to call the whole horde up the tower.",
            order: 961, maxPlayers: 8);
        k.Size(64, 56);
        k.Skybox("night", ("zenith", "#05080e"), ("horizon", "#1e2430"), ("ground", "#101318"), ("stars", 0.9), ("ambient", 2.0),
            ("moon", new JObject(("size", 6), ("intensity", 0.6), ("color", "#d0dcf0"))),
            ("clouds", new JObject(("coverage", 0.6), ("density", 0.5), ("speed", 4), ("color", "#262c38"))),
            ("haze", new JObject(("color", "#30384a"), ("strength", 0.4))));

        Surface();
        // The map's edge: a high cemetery wall (the kit's Perimeter op stands on y 0, under this map's lifted ground).
        WallX(-64, 64, -56, 1, G, 4, "#2a2724");
        WallX(-64, 64, 55, 1, G, 4, "#2a2724");
        WallZ(-55, 55, -64, 1, G, 4, "#2a2724");
        WallZ(-55, 55, 63, 1, G, 4, "#2a2724");
        Nave();
        RoofWalk();
        Tower();
        Transept();
        Choir();
        LadyChapel();
        Vestry();
        ChapterHouse();
        Cloister();
        Barn();
        Close();
        Lodge();
        Graveyard();
        Mausoleum();
        CryptAndOssuary();
        Dungeons();
        PaupersField();
        Dressing();
        BellLogic();
        Spawns();

        CheckLayout();
        if (_problems.Count > 0)
            throw new MapKitException("Ashgrave Cathedral layout:\n  " + string.Join("\n  ", _problems));
        Lift();

        k.Top.Set("zombies", ZombiesSection());
        k.Test(new JObject(("maxPlayers", 8), ("minHills", 0), ("minTeamSpawns", 4), ("handPlaced", false), ("walkFairness", 0.0), ("minMainNavPct", 30), ("navPockets", true),
            ("maxNavBuildMs", 8000), ("minProps", 520), ("minSolidProps", 380), ("minDecals", 600), ("minDecalShare", 90), ("minPackProps", 340),
            ("requireProps", JArray.Of("Great Bell", "Organ Pipe Wall", "Gothic High Altar", "Mystery Box Crate", "Zombies Workbench", "Power Switch Box", "Weapon Locker",
                "Reel Cabinet", "Blackjack Table", "Roulette Table", "High-Low Table", "Dice Table")),
            ("probes", new JArray([
                JArray.Of("nave", -15, -6, G + O), JArray.Of("close", -44, 0, G + O), JArray.Of("cloister walk", -15, 5.8, G + O), JArray.Of("graveyard", 30, 20, G + O),
                JArray.Of("barn", -10, 36, G + O), JArray.Of("lodge", -48, 20, G + O), JArray.Of("chapter house", 10, 12, G + O), JArray.Of("mausoleum", 40, 30, G + O),
                JArray.Of("transept", 10, -20, G + O), JArray.Of("choir", 27, -6, G + O), JArray.Of("lady chapel", 44, -10, G + O), JArray.Of("vestry", 27, 9, G + O),
                JArray.Of("organ loft", -28.0, -9.5, Gal + O), JArray.Of("north gallery", -10, -13, Gal + O), JArray.Of("roof walk", -10, -13, Roof + O),
                JArray.Of("tower base", -29, -21, G + O), JArray.Of("ringing chamber", -29, -19, Roof + O), JArray.Of("belfry", -30.5, -23.5, Belfry + O),
                JArray.Of("crypt", 33, -8, Crypt + O), JArray.Of("ossuary", 6, -10, Crypt + O), JArray.Of("dungeons", 0, 16, Dun + O), JArray.Of("inquisitor", -20, 16, Dun + O),
                JArray.Of("tunnel", 25, 21, Dun + O), JArray.Of("paupers field", -10, -40, G + O)])),
            ("botMatchSeconds", 6), ("spawnsStand", true), ("propsInside", true),
            ("events", new JArray([
                new JObject(("use", "Bell"), ("expect", JArray.Of("Bell.pressed")), ("expectNot", JArray.Of("Climb.started"))),
            ]))));
    }

    /// <summary>Every op, spawn, hill and flag up by <see cref="O"/> (see there).</summary>
    void Lift()
    {
        static void Add(JObject o, string key, bool always = true)
        {
            if (o.Get(key) is JNumber n)
                o.Set(key, Kit.R(n.Value + O));
            else if (always)
                o.Set(key, Kit.R(O));
        }
        foreach (var op in k.Ops)
        {
            switch (op.Str("op"))
            {
                case "Block" or "Collider" or "WallX" or "WallZ":
                    Add(op, "y0");
                    Add(op, "y1");
                    if (op.Get("holes") is JArray holes)
                        foreach (var h in holes.Items.OfType<JObject>())
                        {
                            Add(h, "bottom");
                            Add(h, "top");
                        }
                    break;
                case "Slab":
                    Add(op, "yTop");
                    break;
                case "StairsX" or "StairsZ":
                    Add(op, "y0");
                    break;
                case "Mesh" or "Prop" or "Waypoint" or "Spawn" or "Decal" or "DecalScatter":
                    Add(op, "y");
                    break;
                default:
                    throw new MapKitException($"Lift: an op it doesn't know how to lift ({op.Str("op")})");
            }
        }
        foreach (var team in k.TeamSpawns)
            foreach (var sp in team)
                sp[1] = Kit.R(sp[1] + O);
        foreach (var h in k.Hills)
            Add(h, "y");
        for (var i = 0; i < k.Flags.Count; i++)
            k.Flags[i] = k.Flags[i].Length == 2 ? [k.Flags[i][0], Kit.R(O), k.Flags[i][1]] : [k.Flags[i][0], Kit.R(k.Flags[i][1] + O), k.Flags[i][2]];
    }

    // ==== Building blocks ==========================================================================================

    static double[] Box(double x0, double z0, double x1, double z1, double y0, double y1) => [x0, z0, x1, z1, y0, y1];

    void Area(string name, params double[][] boxes) => _areas.Add((name, boxes.ToList()));

    void Solid(double x0, double z0, double x1, double z1, double y, string what) => _solids.Add((Math.Min(x0, x1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(z0, z1), y, what));

    // Written points and boxes (the zombies and logic sections) are lifted here; the ops by Lift at the end.
    static JArray P(double x, double y, double z) => JArray.Of(Kit.R(x), Kit.R(y + O), Kit.R(z));

    static JArray B6(double x0, double y0, double z0, double x1, double y1, double z1) => JArray.Of(Kit.R(x0), Kit.R(y0 + O), Kit.R(z0), Kit.R(x1), Kit.R(y1 + O), Kit.R(z1));

    /// <summary>A boarded window in a WallX (a wall along X filling z..z+t): <paramref name="side"/> +1 when the area is on the +z side.</summary>
    JObject WinX(string area, double x, double z, double t, int side, double y0 = 0)
    {
        double inZ = side > 0 ? z + t + 0.9 : z - 0.9, outZ = side > 0 ? z - 1.4 : z + t + 1.4, dir = side > 0 ? -1 : 1;
        Window(area, B6(x - WinW / 2, y0 + Sill, z, x + WinW / 2, y0 + WinTop, z + t), (x, outZ), (x, inZ), y0,
            (x - 1.3, outZ + dir * 2.6), (x + 1.3, outZ + dir * 5.0));
        _holes.Add((x - WinW / 2 - 0.05, z - 0.4, x + WinW / 2 + 0.05, z + t + 0.4, y0, $"window at ({x}, {z})"));
        return Kit.Window(x, WinW, y0 + Sill, y0 + WinTop);
    }

    /// <summary>A boarded window in a WallZ (a wall along Z filling x..x+t): <paramref name="side"/> +1 when the area is on the +x side.</summary>
    JObject WinZ(string area, double z, double x, double t, int side, double y0 = 0)
    {
        double inX = side > 0 ? x + t + 0.9 : x - 0.9, outX = side > 0 ? x - 1.4 : x + t + 1.4, dir = side > 0 ? -1 : 1;
        Window(area, B6(x, y0 + Sill, z - WinW / 2, x + t, y0 + WinTop, z + WinW / 2), (outX, z), (inX, z), y0,
            (outX + dir * 2.6, z - 1.3), (outX + dir * 5.0, z + 1.3));
        _holes.Add((x - 0.4, z - WinW / 2 - 0.05, x + t + 0.4, z + WinW / 2 + 0.05, y0, $"window at ({x}, {z})"));
        return Kit.Window(z, WinW, y0 + Sill, y0 + WinTop);
    }

    void Window(string area, JArray box, (double X, double Z) outside, (double X, double Z) inside, double y0, (double X, double Z) s1, (double X, double Z) s2)
    {
        var i = _windows.Count;
        _windows.Add(new JObject(("area", area), ("box", box), ("outside", P(outside.X, y0, outside.Z)), ("inside", P(inside.X, y0, inside.Z)), ("boards", 6)));
        _spawners.Add(new JObject(("window", i), ("at", P(s1.X, y0, s1.Z))));
        _spawners.Add(new JObject(("window", i), ("at", P(s2.X, y0, s2.Z))));
        _keep.Add((outside.X, y0, outside.Z, 1.1, $"window {i} outside"));
        _keep.Add((inside.X, y0, inside.Z, 1.0, $"window {i} inside"));
        _keep.Add((s1.X, y0, s1.Z, 0.9, $"window {i} spawner"));
        _keep.Add((s2.X, y0, s2.Z, 0.9, $"window {i} spawner"));
        k.Waypoint(outside.X, outside.Z, y0);
        k.Waypoint(inside.X, inside.Z, y0);
    }

    /// <summary>A Zombies door or debris in a WallX (along X, filling z..z+t).</summary>
    JObject DoorX(string name, bool debris, int cost, string a, string b, double x, double z, double t, double w, double h, double y0 = 0, bool power = false)
    {
        Door(name, debris, cost, a, b, B6(x - w / 2, y0, z, x + w / 2, y0 + h, z + t), power);
        _keep.Add((x, y0, z - 0.95, 1.1, $"{name} side"));
        _keep.Add((x, y0, z + t + 0.95, 1.1, $"{name} side"));
        _holes.Add((x - w / 2 - 0.05, z - 0.4, x + w / 2 + 0.05, z + t + 0.4, y0, name));
        k.Waypoint(x, z - 1.0, y0);
        k.Waypoint(x, z + t + 1.0, y0);
        return Kit.Door(x, w, h, y0);
    }

    /// <summary>A Zombies door or debris in a WallZ (along Z, filling x..x+t).</summary>
    JObject DoorZ(string name, bool debris, int cost, string a, string b, double z, double x, double t, double w, double h, double y0 = 0, bool power = false)
    {
        Door(name, debris, cost, a, b, B6(x, y0, z - w / 2, x + t, y0 + h, z + w / 2), power);
        _keep.Add((x - 0.95, y0, z, 1.1, $"{name} side"));
        _keep.Add((x + t + 0.95, y0, z, 1.1, $"{name} side"));
        _holes.Add((x - 0.4, z - w / 2 - 0.05, x + t + 0.4, z + w / 2 + 0.05, y0, name));
        k.Waypoint(x - 1.0, z, y0);
        k.Waypoint(x + t + 1.0, z, y0);
        return Kit.Door(z, w, h, y0);
    }

    void Door(string name, bool debris, int cost, string a, string b, JArray box, bool power)
    {
        var o = new JObject(("name", name), ("kind", debris ? "debris" : "door"), ("cost", cost), ("box", box), ("areas", JArray.Of(a, b)));
        if (power)
            o.Add("power", true);
        _doors.Add(o);
    }

    // Machine footprints (x by z before the yaw) and their kinds (docs/zombies/ZOMBIES-MODE.md, "Map format").
    static readonly Dictionary<string, (double W, double D)> Size = new()
    {
        ["box"] = (1.3, 0.66), ["bench"] = (1.6, 0.8), ["perk"] = (0.9, 0.7), ["locker"] = (1.0, 0.6), ["power"] = (0.7, 0.3), ["slots"] = (0.9, 0.7),
        ["blackjack"] = (1.95, 1.25), ["roulette"] = (2.6, 1.15), ["highLow"] = (1.35, 1.35), ["dice"] = (2.05, 1.15),
    };

    static (double X, double Z) Fwd(double yaw) => (Math.Sin(yaw * Math.PI / 180), -Math.Cos(yaw * Math.PI / 180));

    JObject Machine(string kind, double x, double y, double z, double yaw, string what, params (string Key, object? Value)[] more)
    {
        var (w, d) = Size[kind];
        if (Math.Abs(Math.Sin(yaw * Math.PI / 180)) > 0.5)
            (w, d) = (d, w);
        _machines.Add((x - w / 2, z - d / 2, x + w / 2, z + d / 2, y, $"{what} ({kind} at {x}, {z})"));
        var (fx, fz) = Fwd(yaw);
        var reach = kind is "blackjack" or "roulette" or "highLow" or "dice" ? 1.2 : 0.6;
        _keep.Add((x + fx * (Size[kind].D / 2 + reach), y, z + fz * (Size[kind].D / 2 + reach), 0.6, $"{what} front"));
        var o = new JObject(("at", P(x, y, z)), ("yaw", Kit.Yaw(yaw)));
        foreach (var (key, v) in more)
            if (v != null)
                o.Add(key, v);
        k.Waypoint(x + fx * (Size[kind].D / 2 + 1.0), z + fz * (Size[kind].D / 2 + 1.0), y);
        return o;
    }

    void Perk(string perk, double x, double y, double z, double yaw, bool power) =>
        _perks.Add(Machine("perk", x, y, z, yaw, perk, ("perk", perk), ("power", power ? true : null)));

    void Slot(string? category, double x, double y, double z, double yaw) =>
        _slots.Add(Machine("slots", x, y, z, yaw, category ?? "classic slots", ("category", category)));

    void BoxSpot(double x, double y, double z, double yaw) => _box.Add(Machine("box", x, y, z, yaw, "box spot"));

    void Bench(double x, double y, double z, double yaw) => _benches.Add(Machine("bench", x, y, z, yaw, "workbench"));

    void Casino(string game, double x, double y, double z, double yaw) => _casino.Add(Machine(game, x, y, z, yaw, game, ("game", game)));

    void Salvage(double x, double y, double z)
    {
        _salvage.Add(new JObject(("at", P(x, y, z))));
        _keep.Add((x, y, z, 0.6, "salvage"));
    }

    /// <summary>A wall buy on a wall face: (x, z) on the face, <paramref name="yaw"/> the way it faces (into the room), the floor at y.</summary>
    void WallBuy(string build, int cost, double x, double y, double z, double yaw, int? ammo = null)
    {
        var o = new JObject(("build", build), ("at", JArray.Of(Kit.R(x), Kit.R(y + 1.3 + O), Kit.R(z))), ("yaw", Kit.Yaw(yaw)), ("cost", cost));
        if (ammo != null)
            o.Add("ammoCost", ammo.Value);
        _wallBuys.Add(o);
        _wallBuyList.Add((x, y, z, yaw, build));
        var (fx, fz) = Fwd(yaw);
        _keep.Add((x + fx * 0.8, y, z + fz * 0.8, 0.5, $"{build} front"));
    }

    void GroundSpawner(string area, double x, double y, double z)
    {
        _spawners.Add(new JObject(("area", area), ("at", P(x, y, z))));
        _keep.Add((x, y, z, 0.9, $"{area} ground spawner"));
    }

    /// <summary>A solid prop with an approximate footprint for the layout check (half sizes before the yaw).</summary>
    void SProp(string name, double x, double z, double y, double yaw, double hx, double hz)
    {
        k.Prop(name, x, z, y, yaw);
        if (Math.Abs(Math.Sin(yaw * Math.PI / 180)) > 0.5)
            (hx, hz) = (hz, hx);
        Solid(x - hx, z - hz, x + hx, z + hz, y, name);
    }

    /// <summary>Waypoints over a room at height y, about <paramref name="step"/> apart (the nav graph drops the ones in furniture).</summary>
    void Wp(double x0, double z0, double x1, double z1, double y, double step = 3.0) => k.WaypointGrid(x0 + 0.6, z0 + 0.6, x1 - 0.6, z1 - 0.6, y, step);

    // A wall with the cathedral's look: the kit's walls (holes cut), drawn in stone.
    void WallX(double x0, double x1, double z, double t, double y0, double y1, string c, params JObject[] holes) => k.WallX(x0, x1, z, t, y0, y1, c, holes);
    void WallZ(double z0, double z1, double x, double t, double y0, double y1, string c, params JObject[] holes) => k.WallZ(z0, z1, x, t, y0, y1, c, holes);

    // ==== The surface: one slab over the whole map, tiled by region, with the stairwells cut out ======================

    void Surface()
    {
        var holes = new List<(double X0, double Z0, double X1, double Z1)>
        {
            (20.95, 1.3, 28.0, 3.4),   // the crypt stair, in the choir
            (41.4, 28.5, 43.0, 32.6),  // the tunnel stair, in the mausoleum
        };
        var regions = new List<(double X0, double Z0, double X1, double Z1, string Color)>
        {
            (-34, -26, -24, -16, Flag),     // the tower
            (-34, -16, 50, 4, Flag),        // nave, crossing, choir, lady chapel
            (4, -28, 16, -16, Flag),        // the north transept
            (16, 4, 38, 14, FlagDark),      // vestry
            (4, 4, 16, 22, FlagDark),       // chapter house
            (-34, 4, 4, 7.5, Gravel), (-34, 26.1, 4, 29.6, Gravel), (-34, 7.5, -30.1, 26.1, Gravel), (0.1, 7.5, 4, 26.1, Gravel), // the cloister walks
            (-34, 29.6, 16, 44, Earth),     // the barn
            (-54, 14, -42, 26.4, FlagDark), // the lodge
            (34, 28, 44, 38, FlagDark),     // the mausoleum
            (-54.5, -16.5, -34, 44.5, Gravel), // the close
            (-64, -56, 64, 56, Grass),      // everything else
        };
        var done = new List<(double X0, double Z0, double X1, double Z1)>(holes);
        foreach (var r in regions)
        {
            foreach (var piece in Subtract((r.X0, r.Z0, r.X1, r.Z1), done))
                k.Block(piece.X0, -0.6, piece.Z0, piece.X1, 0, piece.Z1, r.Color);
            done.Add((r.X0, r.Z0, r.X1, r.Z1));
        }
        // The cloister's lawn and paths are drawn as decals over the gravel? No: the garth is its own grass (part of "everything else" is not under it), so a lawn block here.
    }

    /// <summary>Rectangle <paramref name="r"/> minus every rectangle in <paramref name="cut"/>: non-overlapping pieces.</summary>
    static List<(double X0, double Z0, double X1, double Z1)> Subtract((double X0, double Z0, double X1, double Z1) r, List<(double X0, double Z0, double X1, double Z1)> cut)
    {
        var pieces = new List<(double X0, double Z0, double X1, double Z1)> { r };
        foreach (var c in cut)
        {
            var next = new List<(double X0, double Z0, double X1, double Z1)>();
            foreach (var p in pieces)
            {
                if (c.X1 <= p.X0 || c.X0 >= p.X1 || c.Z1 <= p.Z0 || c.Z0 >= p.Z1)
                {
                    next.Add(p);
                    continue;
                }
                double ix0 = Math.Max(p.X0, c.X0), ix1 = Math.Min(p.X1, c.X1), iz0 = Math.Max(p.Z0, c.Z0), iz1 = Math.Min(p.Z1, c.Z1);
                if (ix0 > p.X0) next.Add((p.X0, p.Z0, ix0, p.Z1));
                if (ix1 < p.X1) next.Add((ix1, p.Z0, p.X1, p.Z1));
                if (iz0 > p.Z0) next.Add((ix0, p.Z0, ix1, iz0));
                if (iz1 < p.Z1) next.Add((ix0, iz1, ix1, p.Z1));
            }
            pieces = next;
        }
        return pieces.Where(p => p.X1 - p.X0 > 0.01 && p.Z1 - p.Z0 > 0.01).ToList();
    }

    // ==== The nave (start), its aisles and the galleries over them ====================================================

    void Nave()
    {
        // Walls: the north aisle wall (windows east of the tower; the tower door and, at the roof walk, the roof door),
        // the south aisle wall (the cloister door), the west front (the west doors), the rood screen (debris to the crossing).
        WallX(-34, 4, -16, T, G, 12, StoneW,
            DoorX("the Tower Door", false, 1250, "Nave", "Bell Tower", -28.5, -17, 1.6, 1.8, 2.6),
            WinX("Nave", -19, -16, T, 1), WinX("Nave", -12, -16, T, 1), WinX("Nave", -5, -16, T, 1), WinX("Nave", 1.0, -16, T, 1),
            Kit.Hole(-26.0, 2.0, Roof, 12)); // (the roof door: its box spans this wall and the tower's, DoorX in Tower())
        WallX(-34, 4, 3.4, T, G, 12, StoneW, DoorX("the Cloister Door", false, 750, "Nave", "Cloister", -6, 3.4, T, 2.0, 3.0));
        WallZ(-16, 4, -34, T, G, 18, StoneL, DoorZ("the West Doors", false, 750, "Nave", "Close", -6, -34, T, 3.0, 4.2));
        WallZ(-15.4, 3.4, 3.4, T, G, 18, StoneW, DoorZ("the Rood Screen", true, 1000, "Nave", "Transept", -6, 3.4, T, 3.0, 4.0));
        // The clerestory walls over the arcades and the roofs: the high nave roof, the galleries' ceilings (the roof walk's floor).
        WallX(-33.4, 3.4, -10.8, T, 10.4, 18, StoneW);
        WallX(-33.4, 3.4, -1.8, T, 10.4, 18, StoneW);
        k.Slab(-34, -10.8, 4, -1.2, 18.6, 0.6, Slate);
        k.Slab(-34, -16, 4, -10.8, Roof, 0.4, Lead);
        k.Slab(-34, -1.2, 4, 4, Roof, 0.4, Lead);
        // Gallery floors (y 6): north and south over the aisles, the organ loft over the west door; the loft stair comes up
        // through the south gallery (its landing is the nave's, behind the Loft Stair debris).
        k.Slab(-33.4, -15.4, 3.4, -10.5, Gal, 0.5, FlagDark);
        k.Slab(-33.4, -1.5, 3.4, 3.4, Gal, 0.5, FlagDark, (-20.55, 1.4, -12.6, 3.4));
        k.Slab(-33.4, -10.5, -27.0, -1.5, Gal, 0.5, FlagDark);
        // The loft stair: in a walled alcove along the south aisle (the Loft Stair debris across its mouth at the east end),
        // up westwards to the south gallery: two flights of the game's steps (8 and 7) with a landing between, top at x -20.55.
        // (A flight is at most about 11 steps: the nav links a flight's foot to its top, and can't stand a node on a tread.)
        k.StairsX(1.4, 3.4, -12.6, -1, G, 3.2, StoneD);
        k.Block(-20.55, G, 1.4, -16.2, 3.2, 3.4, StoneD); // the landing (and under the upper flight)
        k.StairsX(1.4, 3.4, -17.4, -1, 3.2, Gal - 3.2, StoneD);
        k.Waypoint(-12.0, 2.4); k.Waypoint(-16.8, 2.4, 3.2); k.Waypoint(-21.2, 2.4, Gal);
        WallX(-20.6, -11.0, 1.0, 0.4, G, Gal - 0.5, StoneD); // the alcove's wall along the aisle (up to the gallery floor)
        WallZ(1.4, 3.4, -11.4, 0.4, G, Gal - 0.5, StoneD, DoorZ("the Loft Stair", true, 1000, "Organ Loft", "Nave", 2.4, -11.4, 0.4, 1.8, 2.6));
        Structures.Railing(k, -20.55, 1.2, Gal, -12.4, 1.2);
        Structures.Railing(k, -12.4, 1.4, Gal, -12.4, 3.4);
        // Pillars of the arcades (and the gallery piers over them), the gallery and loft railings.
        foreach (var x in new[] { -27.0, -21.0, -15.0, -9.0, -3.0, 2.4 })
            foreach (var z in new[] { -10.5, -1.5 })
            {
                k.Block(x - 0.45, G, z - 0.45, x + 0.45, Gal - 0.5, z + 0.45, StoneL);
                k.Block(x - 0.4, Gal, z - 0.4, x + 0.4, 10.4, z + 0.4, StoneL);
                Solid(x - 0.45, z - 0.45, x + 0.45, z + 0.45, G, "pillar");
                Solid(x - 0.45, z - 0.45, x + 0.45, z + 0.45, Gal, "pier");
            }
        Structures.Railing(k, -27.0, -10.6, Gal, 3.4, -10.6);
        Structures.Railing(k, -27.0, -1.4, Gal, 3.4, -1.4);
        Structures.Railing(k, -26.9, -10.5, Gal, -26.9, -1.5);

        // (The organ loft first: its stair alcove lies inside the nave's box, and the first area listed wins.)
        Area("Organ Loft", Box(-33.4, -15.4, 3.4, -10.5, 5.5, 10.3), Box(-33.4, -1.5, 3.4, 3.4, 5.5, 10.3), Box(-33.4, -10.5, -27.0, -1.5, 5.5, 10.3),
            Box(-20.6, 1.4, -11.4, 3.4, -0.5, 7.6));
        Area("Nave", Box(-33.4, -15.4, 3.4, 3.4, -0.5, 5.4));

        // ---- The nave's dressing: pews, the start room's machines, confessionals and the high-low table ----
        for (var x = -22.0; x <= -6.1; x += 1.25)
        {
            SProp("Cathedral Pew", x, -8.4, G, 90, 1.5, 0.3);
            SProp("Cathedral Pew", x, -3.6, G, 90, 1.5, 0.3);
        }
        Perk("revive", -27.6, G, 2.95, 0, false);
        Perk("secondwind", -26.3, G, 2.95, 0, false);
        Perk("hammerhand", -25.0, G, 2.95, 0, false);
        BoxSpot(-9.0, G, -15.03, 180);
        Slot(null, -2.5, G, -14.65, 180);
        SProp("Confessional Booth", -22.8, -14.7, G, 0, 1.4, 0.7);
        SProp("Confessional Booth", -25.6, -14.7, G, 0, 1.4, 0.7);
        Casino("highLow", -18.2, G, -12.6, 180);
        WallBuy("Wall Rifle", 500, -33.37, G, 0.5, 90, 250);
        WallBuy("Wall Pistol", 300, 3.37, G, -12.5, -90, 150);
        WallBuy("Buzzbox Machine Pistol", 600, -8.5, G, 3.37, 0);
        SProp("Stone Pulpit", 1.2, -8.8, G, -90, 0.8, 0.8);
        SProp("Brass Lectern", 1.4, -3.2, G, -90, 0.3, 0.25);
        SProp("Votive Candle Stand", -30.5, -14.9, G, 0, 0.65, 0.25);
        SProp("Votive Candle Stand", -31.5, 2.9, G, 180, 0.65, 0.25);
        SProp("Baptismal Font", -31.0, -9.6, G, 0, 0.6, 0.6);
        foreach (var x in new[] { -24.0, -18.0, -12.0, -6.0, 0.0 })
            k.Prop("Iron Chandelier", x, -6.0, 8.0);
        foreach (var x in new[] { -24.0, -18.0, -12.0, -6.0, 0.0 })
        {
            k.Prop("Wall Torch", x + 3, -15.4, 2.6, 0);
            k.Prop("Wall Torch", x + 3, 3.4, 2.6, 180);
        }
        foreach (var x in new[] { -24.0, -16.0, -8.0, 0.0 })
        {
            k.Prop("Lancet Window Blue", x + 1.5, -10.2, 12.0, 0);
            k.Prop("Lancet Window Red", x + 1.5, -1.8, 12.0, 180);
        }
        k.Prop("Rose Window", -33.4, -6.0, 12.6, 90);
        k.Prop("Hanging Tapestry", -33.4, -12.5, 1.0, 90);
        k.Prop("Hanging Tapestry", -33.4, 0.5, 1.0, 90);
        k.DecalScatter(["Floor Cracks", "Water Stain", "Dirt Patch", "Gib Smear", "Rubble Dust"], -33, -15, 3, 3, 60, 11, gap: 1.2);
        k.DecalScatter(["Mould", "Water Damage", "Wall Cracks", "Grime Streaks", "Handprints"], -33.4, -15.4, 3.4, 3.4, 40, 12, onto: "wall");
        Wp(-33.4, -15.4, 3.4, 3.4, G, 2.6);

        // ---- The organ loft and galleries ----
        SProp("Organ Pipe Wall", -32.75, -6.0, Gal, 90, 3.6, 0.6);
        SProp("Organ Console", -30.1, -6.0, Gal, 90, 0.9, 0.55);
        Perk("doubletap", -20.0, Gal, -15.05, 180, true);
        BoxSpot(-4.0, Gal, 3.07, 0);
        WallBuy("Courier Carbine", 900, -8.0, Gal, -15.37, 180);
        WallBuy("Prairie Lever", 1000, 0.0, Gal, 3.37, 0);
        GroundSpawner("Organ Loft", -29.5, Gal, -9.0);
        foreach (var x in new[] { -30.0, -24.0, -14.0, -2.0 })
            SProp("Choir Stalls", x, -14.85, Gal, 0, 2.0, 0.5);
        k.DecalScatter(["Floor Cracks", "Dirt Patch", "Gib Spatter"], -33, -15, 3, -10.6, 14, 21, y: Gal, gap: 1.4);
        k.DecalScatter(["Floor Cracks", "Water Stain"], -33, -1.4, 3, 3, 12, 22, y: Gal, gap: 1.4);
        Wp(-33.4, -15.4, 3.4, -10.5, Gal, 2.4);
        Wp(-33.4, -1.5, 3.4, 1.0, Gal, 2.4);
        Wp(-33.4, -10.5, -27.0, -1.5, Gal, 2.4);
    }

    // ==== The roof walk (outside, on the north aisle's roof) ===========================================================

    void RoofWalk()
    {
        Area("Roof Walk", Box(-33.4, -15.4, 3.4, -10.8, Roof - 0.5, Roof + 3.2));
        Perk("windfall", -10.0, Roof, -11.15, 0, true);
        BoxSpot(-2.0, Roof, -11.13, 0);
        WallBuy("Reaper Marksman", 1500, -17.0, Roof, -10.83, 0);
        GroundSpawner("Roof Walk", -30.5, Roof, -13.0);
        GroundSpawner("Roof Walk", 1.0, Roof, -13.0);
        foreach (var x in new[] { -21.0, -13.0, -5.0, 1.5 })
            k.Prop("Stone Gargoyle", x, -15.95, 12.0, 180);
        foreach (var x in new[] { -26.0, -6.0 })
            SProp("Iron Brazier", x, -11.4, Roof, 0, 0.35, 0.35);
        k.DecalScatter(["Moss Patch", "Puddle", "Leaf Litter", "Gib Smear"], -33, -15.2, 3, -11, 20, 191, y: Roof, gap: 1.4);
        Wp(-33.4, -15.4, 3.4, -10.8, Roof, 2.4);
    }

    // ==== The bell tower =============================================================================================

    void Tower()
    {
        const double t = 1.0;
        // The shaft walls (solid to the belfry's parapet at 19.2), then the belfry stage with an arch on every side.
        WallX(-34, -24, -26, t, G, 19.2, StoneW);
        WallX(-34, -24, -17, t, G, 19.2, StoneW, Kit.Door(-28.5, 1.8, 2.6),
            DoorX("the Roof Door", false, 1500, "Roof Walk", "Bell Tower", -26.0, -17, 1.6, 2.0, 2.4, Roof)); // (wider than the two walls are thick: the door's thin side is across them)
        WallZ(-26, -16, -34, t, G, 19.2, StoneW);
        WallZ(-26, -16, -25, t, G, 19.2, StoneW);
        WallX(-34, -24, -26, t, 19.2, 22.6, StoneL, Kit.Hole(-29, 5.0, 19.2, 21.8));
        WallX(-34, -24, -17, t, 19.2, 22.6, StoneL, Kit.Hole(-29, 5.0, 19.2, 21.8));
        WallZ(-26, -16, -34, t, 19.2, 22.6, StoneL, Kit.Hole(-21, 5.0, 19.2, 21.8));
        WallZ(-26, -16, -25, t, 19.2, 22.6, StoneL, Kit.Hole(-21, 5.0, 19.2, 21.8));
        k.Slab(-34, -26, -24, -16, 23.2, 0.6, Slate);
        // The spire and the corner pinnacles (cosmetic).
        k.Mesh("cone", -29, -21, 23.2, Slate, width: 7.5, height: 12, depth: 7.5, segments: 8);
        foreach (var (x, z) in new[] { (-33.4, -25.4), (-24.6, -25.4), (-33.4, -16.6), (-24.6, -16.6) })
            k.Mesh("cone", x, z, 22.6, StoneL, width: 1.0, height: 3.5, depth: 1.0, segments: 6);

        // Floors: the ringing chamber (10.8; the west strip open over the third flight) and the belfry (18; the east strip
        // open over the fifth).
        k.Slab(-33, -25, -25, -17, Roof, 0.4, FlagDark, (-33.0, -23.7, -31.7, -18.3));
        k.Slab(-33, -25, -25, -17, Belfry, 0.4, FlagDark, (-26.3, -23.7, -25.0, -18.3));
        // The stairs round the walls: five flights of 12 risers of 0.3 m, landings in the corners.
        const double w = 1.3, rise = 3.6;
        var c = StoneD;
        Structures.Stairs(k, -25.65, -18.3, G, rise, 0, -1, width: w, steps: 12, tread: 0.45, color: c);        // east wall, north
        Landing(-26.3, -25.0, -25.0, -23.7, rise);
        Structures.Stairs(k, -26.3, -24.35, rise, rise, -1, 0, width: w, steps: 12, tread: 0.45, color: c);     // north wall, west
        Landing(-33.0, -25.0, -31.7, -23.7, 2 * rise);
        // West wall, south, up to the chamber: steeper (0.4 m risers) with a landing halfway, so a zombie part-way up stands
        // nearest a node on its own flight (the nav can't stand a node on a tread).
        Structures.Stairs(k, -32.35, -23.7, 2 * rise, 2.0, 0, 1, width: w, steps: 5, tread: 0.45, color: c);
        Landing(-33.0, -21.45, -31.7, -20.1, 2 * rise + 2.0);
        Structures.Stairs(k, -32.35, -20.1, 2 * rise + 2.0, Roof - 2 * rise - 2.0, 0, 1, width: w, steps: 4, tread: 0.45, color: c);
        Structures.Stairs(k, -31.7, -17.65, Roof, rise, 1, 0, width: w, steps: 12, tread: 0.45, color: c);      // south wall, east
        Landing(-26.3, -18.3, -25.0, -17.0, Roof + rise);
        Structures.Stairs(k, -25.65, -18.3, Roof + rise, rise, 0, -1, width: w, steps: 12, tread: 0.45, color: c); // east wall, north: the belfry
        // Railings on the shaft side of the flights and round the floors' openings: a little off the flights, and stopping
        // short of the corners where the next flight starts (nothing over a step; the Stairs rule).
        double Up(double from, double along) => from + rise * along / 5.4;
        Structures.Railing(k, -26.5, -18.3, G, -26.5, -23.45, Up(G, 5.15));
        Structures.Railing(k, -26.55, -23.5, Up(rise, 0.25), -31.45, -23.5, Up(rise, 5.15));
        // (Both stop 1.3 m short of the third flight's top: the way between the chamber and the flights is over its last steps.)
        Structures.Railing(k, -31.5, -23.45, 2 * rise + 2.0 * 0.25 / 2.25, -31.5, -21.45, 2 * rise + 2.0);
        Structures.Railing(k, -31.5, -21.45, 2 * rise + 2.0, -31.5, -20.1, 2 * rise + 2.0);
        Structures.Railing(k, -31.5, -20.1, 2 * rise + 2.0, -31.5, -19.6, 2 * rise + 2.0 + 1.6 * 0.5 / 1.8);
        // A stone screen along the chamber's side of the opening (a zombie that sees the stair's foot over a low rail heads
        // straight for it), open over the flight's last 1.3 m.
        k.Block(-31.7, Roof, -23.7, -31.4, Belfry - 0.4, -19.6, StoneW);
        Structures.Railing(k, -33.0, -23.9, Roof, -31.6, -23.9);
        Structures.Railing(k, -30.2, -18.5, Up(Roof, 1.5), -26.55, -18.5, Up(Roof, 5.15)); // (open over its first steps: the way from the landing into the chamber)
        Structures.Railing(k, -26.5, -18.55, Up(Roof + rise, 0.25), -26.5, -23.7, Belfry);
        Structures.Railing(k, -26.7, -23.7, Belfry, -26.7, -18.3);
        Structures.Railing(k, -26.3, -18.1, Belfry, -25.0, -18.1);

        Area("Bell Tower", Box(-33, -25, -25, -17, -0.5, 23));

        // The ground floor: a ground spawner, a wall buy; the ringing chamber: the roulette wheel and Marksman Malt;
        // the belfry: the great bell (the logic's button is at its rope).
        GroundSpawner("Bell Tower", -29.6, G, -21.4);
        WallBuy("Elephant Gun", 1750, -32.97, G, -21.0, 90);
        k.Prop("Wall Torch", -29, -25.0, 2.4, 0);
        k.Prop("Iron Brazier", -27.6, -19.4, 0);
        Casino("roulette", -28.9, Roof, -21.6, 180);
        Perk("marksman", -28.6, Roof, -24.6, 180, true);
        GroundSpawner("Bell Tower", -30.6, Roof, -19.3);
        k.Prop("Wall Torch", -29, -17.0, Roof + 2.2, 180);
        SProp("Great Bell", -29.0, -21.2, Belfry, 90, 0.3, 1.5);
        k.Prop("Stone Gargoyle", -33.5, -26.3, 22.6, 180);
        k.Prop("Stone Gargoyle", -24.5, -26.3, 22.6, 180);
        k.Waypoint(-29, -19.5); k.Waypoint(-30.6, -21.5); k.Waypoint(-27.6, -22.5);
        k.Waypoint(-29, -19.0, Roof); k.Waypoint(-30.0, -24.2, Roof); k.Waypoint(-27.2, -24.2, Roof);
        k.Waypoint(-30.3, -19.9, Roof); k.Waypoint(-32.35, -20.8, 2 * rise + 2.0); k.Waypoint(-30.75, -21.8, Roof); k.Waypoint(-26.6, -21.6, Roof); // (the way between the chamber and the flights, over the third flight's last step)
        k.Waypoint(-30.5, -23.6, Belfry); k.Waypoint(-27.5, -23.6, Belfry); k.Waypoint(-30.5, -18.4, Belfry); k.Waypoint(-29, -19.0, Belfry); k.Waypoint(-32.2, -21, Belfry);
    }

    void Landing(double x0, double z0, double x1, double z1, double y)
    {
        k.Collider(x0, y - 0.3, z0, x1, y, z1);
        k.MeshAt("box", (x0 + x1) / 2, y - 0.15, (z0 + z1) / 2, StoneD, width: x1 - x0, height: 0.3, depth: z1 - z0, material: "Concrete");
        k.Waypoint((x0 + x1) / 2, (z0 + z1) / 2, y);
    }

    // ==== The transept and crossing ====================================================================================

    void Transept()
    {
        WallZ(-28, -16, 4.0, T, G, 18, StoneW, WinZ("Transept", -22, 4.0, T, 1));
        WallX(4, 16, -28, T, G, 18, StoneW, WinX("Transept", 7.5, -28, T, 1), WinX("Transept", 12.5, -28, T, 1));
        WallZ(-28, -16, 15.4, T, G, 18, StoneW, WinZ("Transept", -22, 15.4, T, -1));
        WallX(4, 16, 3.4, T, G, 18, StoneW);
        WallZ(-15.4, 3.4, 15.4, T, G, 18, StoneW, DoorZ("the Choir Screen", false, 1250, "Transept", "Choir", -6, 15.4, T, 2.4, 3.2));
        k.Slab(4, -28, 16, 4, 18.6, 0.6, Slate);
        Area("Transept", Box(4.0, -15.4, 15.4, 3.4, -0.5, 8), Box(4.6, -27.4, 15.4, -15.4, -0.5, 8));
        SProp("Baptismal Font", 10.0, -21.0, G, 0, 0.6, 0.6);
        BoxSpot(10.0, G, -27.07, 180);
        WallBuy("Wall SMG", 1000, 4.63, G, -18.5, 90);
        WallBuy("Kingpin Hand Cannon", 750, 15.37, G, -18.5, -90);
        WallBuy("Wall Pump", 900, 10.0, G, 3.37, 0);
        SProp("Saint Statue", 5.0, -26.4, G, 135, 0.5, 0.5);
        SProp("Saint Statue", 14.4, -26.4, G, 225, 0.5, 0.5);
        SProp("Iron Candelabrum", 6.0, -10.0, G, 0, 0.15, 0.15);
        SProp("Iron Candelabrum", 14.0, -10.0, G, 0, 0.15, 0.15);
        SProp("Iron Candelabrum", 6.0, -2.0, G, 0, 0.15, 0.15);
        SProp("Iron Candelabrum", 14.0, -2.0, G, 0, 0.15, 0.15);
        k.Prop("Iron Chandelier", 10, -6, 9.0);
        k.Prop("Iron Chandelier", 10, -20, 9.0);
        k.Prop("Lancet Window Red", 10.0, -27.4, 6.0, 180);
        k.Prop("Rose Window", 10.0, -27.4, 11.5, 180);
        k.DecalScatter(["Floor Cracks", "Water Stain", "Gib Smear", "Dirt Patch"], 4.5, -27, 15, 3, 30, 31, gap: 1.3);
        Wp(4.0, -27.4, 15.4, 3.4, G, 2.6);
    }

    // ==== The choir ====================================================================================================

    void Choir()
    {
        WallX(16, 38, -16, T, G, 16, StoneW, WinX("Choir", 22, -16, T, 1), WinX("Choir", 32, -16, T, 1));
        WallX(16, 38, 3.4, T, G, 16, StoneW, DoorX("the Vestry Door", false, 1000, "Choir", "Vestry", 31, 3.4, T, 1.8, 2.8));
        WallZ(-15.4, 3.4, 37.4, T, G, 16, StoneW, DoorZ("the Reliquary Grate", false, 1500, "Choir", "Lady Chapel", -6, 37.4, T, 2.4, 3.2, power: true));
        k.Slab(16, -16, 38, 4, 16.6, 0.6, Slate);
        // The crypt stair: down eastwards along the south wall (flights of 6 and 7 of the game's steps, a landing between),
        // walled round its opening.
        k.StairsX(1.3, 3.4, 28.0, -1, Crypt, 2.7, StoneD);
        k.Block(20.95, Crypt, 1.3, 24.85, Crypt + 2.7, 3.4, StoneD); // the landing (and under the upper flight)
        k.StairsX(1.3, 3.4, 23.65, -1, Crypt + 2.7, -Crypt - 2.7, StoneD);
        k.Waypoint(20.4, 2.3); k.Waypoint(24.25, 2.3, Crypt + 2.7); k.Waypoint(28.7, 2.3, Crypt);
        // A stone parapet round the opening, too tall to vault or jump (the mover's mantle reach is 1.7 m): over a low rail
        // the horde found the 5 m drop to the stair's foot shorter than the stair and piled up at the rail.
        WallX(20.95, 28.35, 1.1, 0.2, G, G + 2.6, StoneW);
        WallZ(1.0, 3.1, 28.25, 0.2, G, G + 2.6, StoneW);
        Solid(20.95, 1.2, 28.15, 3.4, G, "the crypt stair's opening");
        Area("Choir", Box(16.0, -15.4, 37.4, 3.4, -0.5, 8), Box(20.7, 1.3, 29.4, 3.4, -5.5, -0.5));
        SProp("Gothic High Altar", 33.5, -6.0, G, -90, 1.4, 0.6);
        SProp("Iron Candelabrum", 33.7, -9.0, G, 0, 0.15, 0.15);
        SProp("Iron Candelabrum", 33.7, -3.0, G, 0, 0.15, 0.15);
        foreach (var x in new[] { 19.0, 23.2, 27.4 })
            SProp("Choir Stalls", x, -11.4, G, 0, 2.0, 0.5);
        foreach (var x in new[] { 23.2, 27.4 }) // (the south row stops short of the crypt stair's head: the way down)
            SProp("Choir Stalls", x, -0.6, G, 180, 2.0, 0.5);
        k.Waypoint(19.6, 0.3); k.Waypoint(30.2, 0.4);
        Perk("jugg", 34.5, G, -15.05, 180, true);
        BoxSpot(27.0, G, -15.07, 180);
        WallBuy("Ratchet PDW", 1100, 18.5, G, 3.37, 0);
        WallBuy("Riot Control", 1250, 37.37, G, -12.0, -90);
        k.Prop("Iron Chandelier", 21, -6, 8.0);
        k.Prop("Iron Chandelier", 29, -6, 8.0);
        k.Prop("Lancet Window Blue", 27, -15.4, 6.0, 0);
        k.Prop("Hanging Tapestry", 37.4, -1.5, 1.0, -90);
        k.DecalScatter(["Floor Cracks", "Water Stain", "Gib Spatter", "Dirt Patch"], 16.5, -15, 37, 1.0, 30, 41, gap: 1.3);
        Wp(16.0, -15.4, 37.4, 1.0, G, 2.6);
    }

    // ==== The Lady Chapel ===============================================================================================

    void LadyChapel()
    {
        WallX(38, 50, -16, T, G, 12, StoneW, WinX("Lady Chapel", 44, -16, T, 1));
        WallZ(-16, 4, 49.4, T, G, 12, StoneW, WinZ("Lady Chapel", -11, 49.4, T, -1), WinZ("Lady Chapel", -1, 49.4, T, -1));
        WallX(38, 50, 3.4, T, G, 12, StoneW);
        k.Slab(38, -16, 50, 4, 12.6, 0.6, Slate);
        Area("Lady Chapel", Box(38.0, -15.4, 49.4, 3.4, -0.5, 8));
        SProp("Saint Statue", 44.0, -6.0, G, -90, 0.5, 0.5);
        SProp("Votive Candle Stand", 41.5, -8.0, G, 90, 0.65, 0.25);
        SProp("Votive Candle Stand", 41.5, -4.0, G, 90, 0.65, 0.25);
        Perk("bulwark", 40.0, G, -15.05, 180, true);
        BoxSpot(43.0, G, 3.07, 0);
        WallBuy("Bastille Squad LMG", 2000, 49.37, G, -6.0, -90);
        foreach (var z in new[] { -13.0, 1.0 })
        {
            SProp("Cathedral Pew", 45.5, z, G, 90, 1.5, 0.3);
            SProp("Cathedral Pew", 46.8, z, G, 90, 1.5, 0.3);
        }
        k.Prop("Rose Window", 49.4, -6.0, 6.5, -90);
        k.Prop("Iron Chandelier", 44, -10, 7.0);
        k.Prop("Iron Chandelier", 44, -2, 7.0);
        k.DecalScatter(["Floor Cracks", "Water Stain", "Gib Smear"], 38.5, -15, 49, 3, 16, 51, gap: 1.3);
        Wp(38.0, -15.4, 49.4, 3.4, G, 2.6);
    }

    // ==== The vestry (sacristy) ========================================================================================

    void Vestry()
    {
        WallX(16, 38, 13.6, 0.4, G, 5, StoneD, DoorX("the Sexton's Gate", true, 1250, "Vestry", "Graveyard", 27, 13.6, 0.4, 2.4, 2.6));
        WallZ(4, 14, 37.6, 0.4, G, 5, StoneD);
        k.Slab(16, 4, 38, 14, 5.4, 0.4, Slate);
        Area("Vestry", Box(16.0, 4.0, 37.6, 13.6, -0.5, 5));
        Casino("blackjack", 21.5, G, 9.2, 0);
        Perk("speed", 37.25, G, 6.5, -90, true);
        Slot("Ammo", 33.0, G, 12.85, 0);
        Slot("Feed", 34.0, G, 12.85, 0);
        WallBuy("Heavy Hitter", 1400, 25.0, G, 4.03, 180);
        GroundSpawner("Vestry", 30.0, G, 9.0);
        foreach (var x in new[] { 18.0, 19.0, 35.0 })
            SProp("Bookshelf", x, 13.3, G, 180, 0.45, 0.16);
        SProp("Scriptorium Desk", 27.5, 8.0, G, 0, 0.65, 0.4);
        k.Prop("Hanging Tapestry", 16.0, 9.0, 1.0, 90);
        k.Prop("Iron Chandelier", 22, 9, 3.4);
        k.Prop("Wall Torch", 33, 4.0, 2.4, 180);
        k.DecalScatter(["Floor Cracks", "Water Stain", "Dirt Patch"], 16.5, 4.5, 37, 13, 16, 61, gap: 1.3);
        Wp(16.0, 4.0, 37.6, 13.6, G, 2.6);
    }

    // ==== The chapter house (scriptorium) ===============================================================================

    void ChapterHouse()
    {
        WallX(4, 16, 21.6, 0.4, G, 8, StoneW);
        WallZ(4, 22, 15.6, 0.4, G, 8, StoneW, DoorZ("the Chapter Garden Door", false, 1500, "Chapter House", "Graveyard", 18, 15.6, 0.4, 2.0, 2.8));
        k.Slab(4, 4, 16, 22, 8.4, 0.4, Slate);
        Area("Chapter House", Box(4.0, 4.0, 15.6, 21.6, -0.5, 7));
        Slot("Optic", 6.5, G, 20.85, 0);
        Slot("Utility", 7.6, G, 20.85, 0);
        Slot("Upgrade", 8.7, G, 20.85, 0);
        Bench(12.5, G, 21.0, 0);
        Perk("salvage", 15.25, G, 7.0, -90, false);
        WallBuy("Wide Load", 800, 4.03, G, 8.0, 90);
        GroundSpawner("Chapter House", 10.0, G, 15.0);
        foreach (var (x, z) in new[] { (7.0, 9.5), (10.5, 9.5), (7.0, 12.5), (10.5, 12.5) })
            SProp("Scriptorium Desk", x, z, G, 0, 0.65, 0.4);
        foreach (var z in new[] { 14.5, 15.5, 16.5, 17.5 })
            SProp("Bookshelf", 4.2, z, G, 90, 0.45, 0.16);
        k.Prop("Iron Chandelier", 10, 13, 5.5);
        k.Prop("Lancet Window Blue", 10, 21.6, 3.0, 180);
        k.DecalScatter(["Floor Cracks", "Water Stain", "Gib Smear"], 4.5, 4.5, 15, 21, 16, 71, gap: 1.3);
        Wp(4.0, 4.0, 15.6, 21.6, G, 2.6);
    }

    // ==== The cloister ==================================================================================================

    void Cloister()
    {
        WallZ(4, 30, -34, 0.4, G, 5, StoneD);
        WallZ(4, 30, 3.6, 0.4, G, 5, StoneD, DoorZ("the Chapter Door", false, 1250, "Cloister", "Chapter House", 13, 3.6, 0.4, 2.0, 2.8));
        WallX(-34, 16, 29.6, 0.4, G, 6, StoneD, DoorX("the Barn Door", false, 1000, "Cloister", "Tithe Barn", -15, 29.6, 0.4, 2.4, 3.0));
        // The covered walks round the garth: arcade piers and a lean-to roof (y 4.2).
        for (var x = -30.1; x <= 0.2; x += 3.37)
        {
            k.Block(x - 0.25, G, 7.25, x + 0.25, 4.2, 7.75, StoneL);
            k.Block(x - 0.25, G, 25.85, x + 0.25, 4.2, 26.35, StoneL);
        }
        for (var z = 10.6; z < 25.9; z += 3.1)
        {
            k.Block(-30.35, G, z - 0.25, -29.85, 4.2, z + 0.25, StoneL);
            k.Block(-0.15, G, z - 0.25, 0.35, 4.2, z + 0.25, StoneL);
        }
        k.Slab(-34, 4, 4, 7.75, 4.6, 0.4, Slate);
        k.Slab(-34, 25.85, 4, 29.6, 4.6, 0.4, Slate);
        k.Slab(-34, 7.75, -29.85, 25.85, 4.6, 0.4, Slate);
        k.Slab(-0.15, 7.75, 3.6, 25.85, 4.6, 0.4, Slate);
        Area("Cloister", Box(-33.6, 4.0, 3.6, 29.6, -0.5, 7));
        // The garth: a lawn with a well at its heart, gravestones of the canons, two ground spawners.
        SProp("Stone Well", -15.0, 16.8, G, 0, 0.95, 0.85);
        GroundSpawner("Cloister", -24.0, G, 21.5);
        GroundSpawner("Cloister", -6.0, G, 12.0);
        foreach (var (x, z) in new[] { (-26.0, 11.0), (-22.0, 11.0), (-9.0, 22.0), (-5.0, 22.5), (-26.0, 16.0), (-4.0, 16.5) })
            SProp("Gothic Headstone", x, z, G, 180, 0.4, 0.2);
        k.Prop("Gnarled Dead Tree", -20.0, 23.0);
        Solid(-20.3, 22.7, -19.7, 23.3, G, "tree");
        BoxSpot(-33.27, G, 15.0, 90);
        Salvage(-31.5, G, 27.5);
        WallBuy("Paper Route", 700, -33.57, G, 22.0, 90);
        WallBuy("Sawn-Off Special", 800, -22.0, G, 29.57, 0);
        WallBuy("Pea Shooter", 500, 3.57, G, 24.0, -90);
        foreach (var x in new[] { -25.0, -15.0, -5.0 })
            k.Prop("Hanging Lantern", x, 7.6, 2.0);
        k.DecalScatter(["Leaf Litter", "Moss Patch", "Mud Patch", "Leaf Pile"], -29.5, 8, -0.5, 25.5, 30, 81, gap: 1.4);
        k.DecalScatter(["Floor Cracks", "Moss Patch", "Water Stain"], -33.4, 4.2, 3.4, 7.6, 14, 82, gap: 1.4);
        Wp(-33.6, 4.0, 3.6, 29.6, G, 3.0);
    }

    // ==== The tithe barn (stable) ======================================================================================

    void Barn()
    {
        WallX(-34, 16, 43.6, 0.4, G, 6, BarnW, WinX("Tithe Barn", -25, 43.6, 0.4, -1), WinX("Tithe Barn", -10, 43.6, 0.4, -1), WinX("Tithe Barn", 5, 43.6, 0.4, -1));
        WallZ(30, 44, -34, 0.4, G, 6, BarnW, DoorZ("the Barn West Door", false, 750, "Tithe Barn", "Close", 37, -34, 0.4, 2.4, 3.0));
        WallZ(30, 44, 15.6, 0.4, G, 6, BarnW);
        k.Slab(-34, 29.6, 16, 44, 6.4, 0.4, Timber);
        k.MeshAt("box", -9, 8.2, 33.4, Slate, roll: 0, pitch: 28, width: 50.4, height: 0.3, depth: 8.6);
        k.MeshAt("box", -9, 8.2, 40.2, Slate, roll: 0, pitch: -28, width: 50.4, height: 0.3, depth: 8.6);
        Area("Tithe Barn", Box(-33.6, 30.0, 15.6, 43.6, -0.5, 6));
        foreach (var x in new[] { -30.0, -26.5, -23.0, -19.5 })
            SProp("Horse Stall", x, 41.6, G, 90, 1.5, 0.08);
        SProp("Hay Bale Stack", -15.0, 41.9, G, 0, 1.15, 0.6);
        SProp("Hay Bale", 0.0, 42.6, G, 0, 0.6, 0.4);
        SProp("Hay Wagon", -4.0, 35.5, G, 0, 3.1, 1.1);
        SProp("Tipped Farm Cart", 8.0, 36.5, G, 90, 1.2, 1.2);
        Slot("Muzzle", 10.0, G, 30.75, 180);
        Slot("Stock", 11.0, G, 30.75, 180);
        Slot("Grip", 12.0, G, 30.75, 180);
        Bench(14.8, G, 40.0, -90);
        BoxSpot(-28.0, G, 30.73, 180);
        Salvage(-21.0, G, 32.0);
        WallBuy("High Rider", 600, -33.57, G, 33.0, 90);
        WallBuy("Coach Gun", 1500, -6.0, G, 30.03, 180);
        foreach (var x in new[] { -24.0, -12.0, 0.0, 12.0 })
            k.Prop("Hanging Lantern", x, 31.0, 3.4);
        k.DecalScatter(["Dirt Patch", "Mud Patch", "Leaf Litter", "Gib Smear"], -33, 30.5, 15, 43, 40, 91, gap: 1.3);
        Wp(-33.6, 30.0, 15.6, 43.6, G, 3.0);
    }

    // ==== The close (forecourt and yew walk) ==========================================================================

    void Close()
    {
        WallZ(-16.5, 44.5, -54.5, 0.5, G, 3.2, Precinct,
            WinZ("Close", -8, -54.5, 0.5, 1), WinZ("Close", 4, -54.5, 0.5, 1), WinZ("Lodge", 17, -54.5, 0.5, 1), WinZ("Lodge", 23, -54.5, 0.5, 1), WinZ("Close", 36, -54.5, 0.5, 1));
        WallX(-54.5, -34, -16.5, 0.5, G, 3.2, Precinct, WinX("Close", -44, -16.5, 0.5, 1));
        WallX(-54.5, -33.6, 44.0, 0.5, G, 3.2, Precinct, WinX("Close", -44, 44.0, 0.5, -1));
        Area("Close", Box(-54.0, -16.0, -34.0, 13.6, -0.5, 8), Box(-42.0, 13.6, -34.0, 26.4, -0.5, 8), Box(-54.0, 26.4, -34.0, 44.0, -0.5, 8));
        Perk("fleetfoot", -34.35, G, -12.0, -90, true);
        BoxSpot(-34.33, G, 8.0, -90);
        WallBuy("Burst Marksman", 1200, -34.03, G, 2.5, -90);
        Salvage(-52.0, G, -14.0);
        SProp("Mourning Angel Statue", -44.0, -6.0, G, 90, 0.55, 0.55);
        foreach (var (x, z) in new[] { (-50.0, -12.0), (-38.0, -12.0), (-50.0, 0.0), (-38.0, 12.0), (-48.0, 30.0), (-38.0, 40.0) })
            k.Prop("Lantern Street Lamp", x, z);
        foreach (var (x, z) in new[] { (-50.0, 8.0), (-46.0, 34.0), (-51.0, 41.0), (-38.5, 30.0) })
        {
            k.Prop("Gnarled Dead Tree", x, z);
            Solid(x - 0.3, z - 0.3, x + 0.3, z + 0.3, G, "tree");
        }
        foreach (var (x, z) in new[] { (-48.0, 38.0), (-44.0, 38.0), (-48.0, 34.5), (-52.0, 30.5) })
            SProp("Gothic Headstone", x, z, G, 0, 0.4, 0.2);
        SProp("Iron Brazier", -40.0, -1.5, G, 0, 0.35, 0.35);
        SProp("Iron Brazier", -40.0, -10.5, G, 0, 0.35, 0.35);
        k.DecalScatter(["Leaf Litter", "Mud Patch", "Gravel Scatter", "Puddle", "Leaf Pile"], -54, -16, -34.5, 44, 50, 101, gap: 1.5);
        Wp(-54.0, -16.0, -34.0, 13.6, G, 3.2);
        Wp(-42.0, 13.6, -34.0, 26.4, G, 3.2);
        Wp(-54.0, 26.4, -34.0, 44.0, G, 3.2);
    }

    // ==== The groundskeeper's lodge (the armoury) ======================================================================

    void Lodge()
    {
        WallX(-54, -42, 13.6, 0.4, G, 4, Timber);
        WallX(-54, -42, 26, 0.4, G, 4, Timber);
        WallZ(14, 26, -42.4, 0.4, G, 4, Timber, DoorZ("the Lodge Door", false, 1000, "Lodge", "Close", 20, -42.4, 0.4, 1.6, 2.6));
        k.Slab(-54.5, 13.6, -42, 26.4, 4.4, 0.4, Slate);
        Area("Lodge", Box(-54.0, 14.0, -42.4, 26.0, -0.5, 4));
        Slot("Receiver", -51.0, G, 14.75, 180);
        Slot("Action", -50.0, G, 14.75, 180);
        Slot("Barrel", -49.0, G, 14.75, 180);
        Bench(-48.0, G, 25.2, 0);
        WallBuy("Wall SMG", 1000, -42.43, G, 16.0, -90);
        SProp("Steel Rifle Rack", -45.5, 25.6, G, 180, 0.75, 0.23);
        SProp("Wooden Crate", -44.0, 15.0, G, 0, 0.5, 0.5);
        SProp("Cider Barrel", -53.2, 25.0, G, 0, 0.4, 0.4);
        k.Prop("Hanging Lantern", -48, 20, 2.6);
        k.DecalScatter(["Dirt Patch", "Oil Stain", "Mud Patch"], -53.5, 14.5, -43, 25.5, 12, 111, gap: 1.2);
        Wp(-54.0, 14.0, -42.4, 26.0, G, 2.4);
    }

    // ==== The graveyard ================================================================================================

    void Graveyard()
    {
        WallZ(4, 44.5, 50, 0.5, G, 3.2, Precinct, WinZ("Graveyard", 10, 50, 0.5, -1), WinZ("Graveyard", 22, 50, 0.5, -1), WinZ("Graveyard", 36, 50, 0.5, -1));
        WallX(16, 50.5, 44.0, 0.5, G, 3.2, Precinct, WinX("Graveyard", 24, 44.0, 0.5, -1), WinX("Graveyard", 42, 44.0, 0.5, -1));
        Area("Graveyard", Box(16.0, 14.0, 50.0, 28.0, -0.5, 8), Box(16.0, 28.0, 34.0, 38.0, -0.5, 8), Box(44.0, 28.0, 50.0, 38.0, -0.5, 8),
            Box(16.0, 38.0, 50.0, 44.0, -0.5, 8), Box(38.0, 4.0, 50.0, 14.0, -0.5, 8), Box(4.0, 22.0, 16.0, 29.6, -0.5, 8));
        Casino("dice", 24.0, G, 33.5, 0);
        SProp("Mason's Bench", 20.0, 36.8, G, 0, 1.0, 0.4);
        SProp("Gothic Headstone", 27.6, 36.8, G, 0, 0.4, 0.2);
        BoxSpot(30.0, G, 14.33, 180);
        WallBuy("Bucket Lobber", 2250, 20.0, G, 14.03, 180);
        WallBuy("Auto Shotgun", 1750, 36.0, G, 27.97, 0);
        Salvage(47.0, G, 41.5);
        Salvage(8.0, G, 27.0);
        GroundSpawner("Graveyard", 28.0, G, 24.0);
        GroundSpawner("Graveyard", 45.0, G, 18.0);
        // Rows of graves (headstones, tombs, crosses, angels) clear of the paths, gates, doors and the mausoleum.
        var rng = new Rng(7);
        foreach (var (x0, z0, x1, z1) in new[] { (18.5, 16.5, 26.5, 22.0), (33.0, 16.5, 44.0, 22.0), (18.5, 26.0, 31.0, 30.0), (45.5, 26.0, 48.5, 33.0), (40.0, 40.0, 48.0, 42.5), (17.5, 40.0, 22.0, 42.5) })
            for (var x = x0; x <= x1; x += 2.2)
                for (var z = z0; z <= z1; z += 2.6)
                {
                    var r = rng.Next();
                    var (name, hx, hz) = r < 0.45 ? ("Gothic Headstone", 0.4, 0.2) : r < 0.75 ? ("Churchyard Headstone", 0.35, 0.15) : r < 0.9 ? ("Churchyard Chest Tomb", 1.0, 0.55) : ("Memorial Cross", 0.35, 0.35);
                    SProp(name, x, z, G, 0, hx, hz);
                }
        SProp("Mourning Angel Statue", 40.0, 24.5, G, 0, 0.55, 0.55);
        SProp("Calvary Cross", 45.0, 8.0, G, -90, 0.6, 0.6);
        foreach (var (x, z) in new[] { (17.5, 24.5), (48.0, 15.5), (32.0, 41.0), (12.0, 24.0) })
        {
            k.Prop("Gnarled Dead Tree", x, z);
            Solid(x - 0.3, z - 0.3, x + 0.3, z + 0.3, G, "tree");
        }
        foreach (var (x, z) in new[] { (31.0, 23.5), (44.0, 6.0), (16.5, 32.0), (40.0, 39.0), (6.0, 23.0) })
            k.Prop("Lantern Street Lamp", x, z);
        k.DecalScatter(["Leaf Litter", "Mud Patch", "Moss Patch", "Puddle", "Leaf Pile", "Gib Smear"], 16, 14, 50, 44, 70, 121, gap: 1.5);
        Wp(16.0, 14.0, 50.0, 28.0, G, 3.2);
        Wp(16.0, 28.0, 34.0, 38.0, G, 3.2);
        Wp(44.0, 28.0, 50.0, 38.0, G, 3.0);
        Wp(16.0, 38.0, 50.0, 44.0, G, 3.0);
        Wp(38.0, 4.0, 50.0, 14.0, G, 3.0);
        Wp(4.0, 22.0, 16.0, 29.6, G, 3.0);
    }

    // ==== The mausoleum (and the tunnel stair up into it) ==============================================================

    void Mausoleum()
    {
        WallX(34, 44, 28, 0.5, G, 5, StoneL, DoorX("the Mausoleum Gate", false, 1750, "Graveyard", "Mausoleum", 38.5, 28, 0.5, 2.0, 3.0));
        WallX(34, 44, 37.5, 0.5, G, 5, StoneL);
        WallZ(28, 38, 34, 0.5, G, 5, StoneL);
        WallZ(28, 38, 43.5, 0.5, G, 5, StoneL);
        k.Slab(34, 28, 44, 38, 5.5, 0.5, StoneD);
        k.MeshAt("box", 39, 6.2, 28.3, StoneL, roll: 0, width: 10.4, height: 1.4, depth: 0.6);
        Area("Mausoleum", Box(34.5, 28.5, 43.5, 37.5, -0.5, 5), Box(41.0, 23.0, 43.4, 32.7, Tun - 0.5, 0.4));
        // The tunnel stair: two flights of 8 of the game's steps up from the tunnel's raised end to the mausoleum floor, a
        // landing between, railed round its opening.
        k.StairsZ(41.4, 43.0, 24.2, 1, Tun, -3.2 - Tun, UnderD);
        k.Block(41.4, Tun, 27.8, 43.0, -3.2, 32.6, UnderD); // the landing (and under the upper flight)
        k.StairsZ(41.4, 43.0, 29.0, 1, -3.2, 3.2, UnderD);
        k.Waypoint(42.2, 23.5, Tun); k.Waypoint(42.2, 28.4, -3.2); k.Waypoint(42.2, 33.2);
        Structures.Railing(k, 41.25, 28.5, G, 41.25, 32.6);
        Solid(41.25, 28.5, 43.5, 32.6, G, "the tunnel stair's opening");
        WallZ(22.6, 32.6, 41.0, 0.4, Tun, -0.6, UnderD);
        WallZ(22.6, 32.6, 43.0, 0.4, Tun, -0.6, UnderD);
        _slotUp = Machine("locker", 37.0, G, 37.0, 0, "the slot upgrade", ("cost", 4000));
        Slot("grenade", 35.25, G, 33.5, 90);
        SProp("Stone Sarcophagus", 38.0, 31.5, G, 0, 1.2, 0.55);
        SProp("Stone Sarcophagus", 38.0, 34.5, G, 0, 1.2, 0.55);
        GroundSpawner("Mausoleum", 36.0, G, 30.0);
        k.Prop("Wall Torch", 34.5, 31, 2.2, 90);
        k.Prop("Wall Torch", 34.5, 36, 2.2, 90);
        k.Prop("Stone Gargoyle", 34.4, 27.6, 5.0, 180);
        k.Prop("Stone Gargoyle", 43.6, 27.6, 5.0, 180);
        Wp(34.5, 28.5, 41.0, 37.5, G, 2.2);
    }

    // ==== Under the choir and the crossing: the crypt and the ossuary ===================================================

    void CryptAndOssuary()
    {
        const double ceil = -0.6;
        // The crypt (under the choir): floor, walls, the stair corridor along the south wall, the crypt door at its foot.
        k.Block(16, Crypt - 0.6, -16, 38, Crypt, 4, CryptFloor);
        WallX(16, 38, -16, T, Crypt, ceil, Under);
        WallX(16, 38, 3.4, T, Crypt, ceil, Under);
        WallZ(-16, 4, 37.4, T, Crypt, ceil, Under);
        WallX(16.4, 29.8, 0.9, 0.4, Crypt, ceil, UnderD);
        WallZ(1.3, 3.4, 29.4, 0.4, Crypt, ceil, UnderD, DoorZ("the Crypt Door", false, 1250, "Choir", "Crypt", 2.35, 29.4, 0.4, 2.0, 2.6, Crypt));
        k.Block(16.4, Crypt, 1.3, 20.95, ceil, 3.4, UnderD); // (behind the stair's top: solid)
        // The divider between the ossuary (west) and the crypt: the bone pile (debris).
        WallZ(-15.4, 3.4, 15.6, 0.8, Crypt, ceil, UnderD, DoorZ("the Bone Pile", true, 1250, "Ossuary", "Crypt", -6, 15.6, 0.8, 2.4, 2.6, Crypt));
        foreach (var x in new[] { 21.0, 26.5, 32.0 })
            foreach (var z in new[] { -10.0, -3.5 })
            {
                k.Block(x - 0.4, Crypt, z - 0.4, x + 0.4, ceil, z + 0.4, StoneD);
                Solid(x - 0.4, z - 0.4, x + 0.4, z + 0.4, Crypt, "crypt pillar");
            }
        Area("Crypt", Box(16.4, -15.4, 37.4, 0.9, Crypt - 0.5, -1), Box(29.8, 0.9, 37.4, 3.4, Crypt - 0.5, -1));
        _power = Machine("power", 37.05, Crypt, -10.0, -90, "the power switch");
        Perk("bloodroot", 37.05, Crypt, -3.0, -90, false);
        Bench(26.0, Crypt, -14.8, 180);
        BoxSpot(33.0, Crypt, -15.07, 180);
        WallBuy("Ratchet PDW", 1100, 20.0, Crypt, -15.37, 180);
        GroundSpawner("Crypt", 23.8, Crypt, -9.3);
        GroundSpawner("Crypt", 34.0, Crypt, 1.6);
        foreach (var (x, z) in new[] { (18.8, -12.5), (23.8, -12.5), (29.2, -12.5), (18.8, -1.2), (24.0, -6.8), (29.5, -6.8) })
            SProp("Stone Sarcophagus", x, z, Crypt, 0, 1.25, 0.6);
        foreach (var z in new[] { -13.0, -9.0, -5.0, -1.0 })
            k.Prop("Wall Torch", 16.4, z, Crypt + 2.2, 90);
        SProp("Iron Brazier", 35.0, -6.5, Crypt, 0, 0.35, 0.35);
        k.DecalScatter(["Floor Cracks", "Water Stain", "Moss Patch", "Gib Smear", "Gib Spatter"], 16.5, -15, 37, 0.5, 30, 131, y: Crypt, gap: 1.2);
        k.DecalScatter(["Cobweb", "Ceiling Water Spot"], 16.5, -15, 37, 0.5, 14, 132, y: Crypt, onto: "ceiling");
        Wp(16.4, -15.4, 37.4, 0.9, Crypt, 2.4);
        Wp(29.8, 0.9, 37.4, 3.4, Crypt, 2.0);

        // The ossuary (under the crossing): floor (an opening for the dungeon stair), walls, the stair shaft down to the dungeons.
        k.Slab(3.4, -16, 16.0, 3.4, Crypt, 0.6, CryptFloor, (8.8, -3.05, 11.2, 3.4));
        WallX(3.4, 15.6, -16, T, Crypt, ceil, Under);
        WallX(3.4, 15.6, 3.4, T, Crypt, ceil, Under);
        WallZ(-16, 4, 3.4, T, Crypt, ceil, Under);
        // (Flights of 7 and 6 steps with a landing between; as wide as its shaft: no gap down the sides.)
        k.StairsZ(8.8, 11.2, 4.0, -1, Dun, 2.7, UnderD);
        k.Block(8.8, Dun, -3.05, 11.2, Dun + 2.7, 0.85, UnderD); // the landing (and under the upper flight)
        k.StairsZ(8.8, 11.2, -0.35, -1, Dun + 2.7, Crypt - Dun - 2.7, UnderD);
        k.Waypoint(10, -3.6, Crypt); k.Waypoint(10, 0.25, Dun + 2.7); k.Waypoint(10, 4.8, Dun);
        Structures.Railing(k, 8.65, -3.05, Crypt, 8.65, 3.4);
        Structures.Railing(k, 11.35, -3.05, Crypt, 11.35, 3.4);
        Solid(8.65, -3.05, 11.35, 3.4, Crypt, "the dungeon stair's opening");
        WallZ(-3.05, 5.6, 8.4, 0.4, Dun, Crypt - 0.6, UnderD);
        WallZ(-3.05, 5.6, 11.2, 0.4, Dun, Crypt - 0.6, UnderD);
        k.Block(8.4, -6.2, 3.4, 11.6, -5.8, 5.6, UnderD); // the shaft's ceiling (3 m over the stair's foot)
        k.Block(8.8, Dun - 0.6, 3.4, 11.2, Dun, 5.6, DunFloor);
        Area("Ossuary", Box(4.0, -15.4, 15.6, 3.4, Crypt - 0.5, -1), Box(8.8, -3.2, 11.2, 5.6, Dun - 0.5, Crypt + 0.5));
        foreach (var x in new[] { 5.6, 8.7, 11.8 })
            SProp("Ossuary Niche Wall", x + 0.9, -15.1, Crypt, 0, 1.5, 0.3);
        foreach (var z in new[] { -12.0, -8.7 })
            SProp("Ossuary Niche Wall", 4.3, z, Crypt, 90, 1.5, 0.3);
        Slot("Head", 15.25, Crypt, 0.5, -90);
        Slot("Hilt", 15.25, Crypt, -0.5, -90);
        Slot("MeleeMod", 15.25, Crypt, -1.5, -90);
        BoxSpot(4.33, Crypt, -3.0, 90);
        WallBuy("Wall Pump", 900, 4.03, Crypt, 1.5, 90);
        GroundSpawner("Ossuary", 7.0, Crypt, -8.0);
        foreach (var (x, z) in new[] { (6.0, -5.0), (13.0, -12.0), (12.8, -7.0) })
            k.Prop("Skull Heap", x, z, Crypt);
        k.Prop("Wall Torch", 4.0, -6, Crypt + 2.2, 90);
        k.Prop("Wall Torch", 15.2, -11, Crypt + 2.2, -90);
        k.DecalScatter(["Floor Cracks", "Gib Smear", "Moss Patch"], 4.5, -15, 15, 3, 18, 141, y: Crypt, gap: 1.2);
        Wp(4.0, -15.4, 15.6, -2.0, Crypt, 2.2);
        Wp(4.0, -2.0, 8.6, 3.4, Crypt, 2.0);
        Wp(11.4, -2.0, 15.6, 3.4, Crypt, 2.0);
    }

    // ==== The dungeons, the inquisitor's chamber and the smugglers' tunnel ==============================================

    void Dungeons()
    {
        const double ceil = -6.4;
        k.Block(-26.4, Dun - 0.6, 5.6, 16.4, Dun, 26.4, DunFloor);
        k.Block(-26.4, ceil, 5.6, 16.4, ceil + 0.4, 26.4, UnderD);
        WallX(-26.4, 16.4, 5.6, 0.4, Dun, ceil, Under, DoorX("the Dungeon Gate", false, 1500, "Ossuary", "Dungeons", 10, 5.6, 0.4, 1.6, 2.6, Dun));
        WallX(-26.4, 16.4, 26.0, 0.4, Dun, ceil, Under);
        WallZ(5.6, 26.4, -26.4, 0.4, Dun, ceil, Under);
        WallZ(6.0, 26.0, -14.4, 0.4, Dun, ceil, Under, DoorZ("the Inquisitor's Door", false, 1750, "Inquisitor's Chamber", "Dungeons", 16, -14.4, 0.4, 1.6, 2.6, Dun));
        WallZ(5.6, 26.4, 16.0, 0.4, Dun, ceil, Under, DoorZ("the Tunnel Debris", true, 1500, "Dungeons", "Smugglers' Tunnel", 21.1, 16.0, 0.4, 2.4, 2.4, Dun));
        // Cells either side of the corridor (z 14-18): dividers and bars with an open door in each.
        foreach (var x in new[] { -9.0, -4.0, 1.0, 6.0, 11.0 })
        {
            if (x < 10) // (the guard room by the stair, x 6-16, is one room)
            {
                WallZ(6.0, 14.0, x - 0.2, 0.4, Dun, ceil, UnderD);
                Solid(x - 0.2, 6.0, x + 0.2, 14.0, Dun, "cell wall");
            }
            WallZ(18.0, 26.0, x - 0.2, 0.4, Dun, ceil, UnderD);
            Solid(x - 0.2, 18.0, x + 0.2, 26.0, Dun, "cell wall");
        }
        foreach (var x0 in new[] { -14.0, -9.0, -4.0, 1.0, 6.0, 11.0 })
        {
            var x1 = Math.Min(x0 + 5, 16.0);
            if (x0 < 5) // (the guard room by the stair stays open)
                SProp("Cell Bars", x0 + 1.7, 14.0, Dun, 0, 1.5, 0.08);
            SProp("Cell Bars", x0 + 1.7, 18.0, Dun, 0, 1.5, 0.08);
        }
        Area("Dungeons", Box(-14.0, 6.0, 16.0, 26.0, Dun - 0.5, ceil - 0.2));
        Bench(-6.5, Dun, 6.6, 180);
        BoxSpot(3.5, Dun, 25.27, 0);
        WallBuy("Wall Rifle", 500, -13.97, Dun, 9.0, 90, 250);
        Salvage(-11.5, Dun, 23.0);
        GroundSpawner("Dungeons", -11.5, Dun, 9.5);
        GroundSpawner("Dungeons", 13.5, Dun, 22.5);
        SProp("Iron Cage", -1.5, 8.0, Dun, 0, 0.65, 0.65);
        SProp("Pillory", 8.5, 22.5, Dun, 0, 0.7, 0.15);
        foreach (var x in new[] { -12.0, -2.0, 8.0 })
            k.Prop("Wall Torch", x, 14.0, Dun + 2.0, 180);
        foreach (var x in new[] { -7.0, 3.0, 13.0 })
            k.Prop("Wall Torch", x, 18.0, Dun + 2.0, 0);
        k.Prop("Gibbet Cage", 3.5, 10.0, Dun + 1.4);
        k.DecalScatter(["Floor Cracks", "Water Stain", "Moss Patch", "Gib Smear", "Gib Spatter", "Puddle"], -14, 6, 16, 26, 40, 151, y: Dun, gap: 1.2);
        Wp(-14.0, 14.0, 16.0, 18.0, Dun, 2.0);
        foreach (var x0 in new[] { -14.0, -9.0, -4.0, 1.0, 6.0, 11.0 })
        {
            k.Waypoint(x0 + 2.5, 10.0, Dun);
            k.Waypoint(x0 + 2.5, 22.0, Dun);
            k.Waypoint(x0 + 4.2, 14.6, Dun);
            k.Waypoint(x0 + 4.2, 17.4, Dun);
            k.Waypoint(x0 + 5.0, 18.9, Dun); // (inside each cell's door, so a zombie lines up with the gap in the bars)
            if (x0 < 5)
                k.Waypoint(x0 + 5.0, 13.1, Dun);
        }

        // The inquisitor's chamber (west).
        Area("Inquisitor's Chamber", Box(-26.0, 6.0, -14.4, 26.0, Dun - 0.5, ceil - 0.2));
        SProp("Stretching Rack", -20.0, 12.0, Dun, 0, 1.15, 0.5);
        SProp("Iron Cage", -24.5, 23.5, Dun, 0, 0.65, 0.65);
        SProp("Iron Brazier", -17.0, 15.0, Dun, 0, 0.35, 0.35);
        SProp("Iron Brazier", -23.0, 18.0, Dun, 0, 0.35, 0.35);
        k.Prop("Gibbet Cage", -20.0, 21.0, Dun + 1.6);
        Slot("Multiplier", -25.25, Dun, 9.0, 90);
        Slot("Splitter", -25.25, Dun, 10.0, 90);
        GroundSpawner("Inquisitor's Chamber", -20.0, Dun, 18.5);
        WallBuy("Kingpin Hand Cannon", 750, -25.97, Dun, 16.0, 90);
        k.DecalScatter(["Gib Smear", "Gib Spatter", "Floor Cracks", "Water Stain"], -25.5, 6.5, -15, 25.5, 20, 161, y: Dun, gap: 1.2);
        Wp(-26.0, 6.0, -14.4, 26.0, Dun, 2.4);

        // The smugglers' tunnel (east, under the graveyard) to the mausoleum stair.
        // Its east end climbs a flight of 9 steps (x 30-34.05) to a raised floor at Tun, so the tomb stair is two flights.
        k.Block(16.4, Dun - 0.6, 19.6, 43.4, Dun, 22.6, DunFloor);
        k.StairsX(19.6, 22.6, 30.0, 1, Dun, Tun - Dun, UnderD);
        k.Block(34.05, Dun, 19.6, 43.4, Tun, 22.6, DunFloor);
        k.Block(16.4, -7.2, 19.2, 29.4, -6.8, 23.0, UnderD);
        k.Block(29.4, Tun + 2.8, 19.2, 43.4, Tun + 3.2, 23.0, UnderD);
        k.Block(29.2, -6.8, 19.2, 29.4, Tun + 2.8, 23.0, UnderD); // (closes the step in the ceiling)
        WallX(16.4, 43.4, 19.2, 0.4, Dun, Tun + 3.2, Under);
        WallX(16.4, 41.0, 22.6, 0.4, Dun, Tun + 3.2, Under);
        WallX(41.0, 43.4, 22.6, 0.4, Tun, Tun + 3.2, UnderD, DoorX("the Tomb Stair", false, 1000, "Smugglers' Tunnel", "Mausoleum", 42.2, 22.6, 0.4, 1.6, 2.4, Tun));
        WallZ(19.2, 23.0, 43.4, 0.4, Dun, Tun + 3.2, Under);
        WallX(41.0, 43.4, 22.6, 0.4, Tun + 3.2, -0.6, UnderD); // (the shaft's end over the tomb stair's door)
        k.Block(41.0, Dun - 0.6, 23.0, 43.4, Tun, 24.2, DunFloor);
        Area("Smugglers' Tunnel", Box(16.4, 19.6, 43.4, 22.6, Dun - 0.5, Tun + 2.6));
        foreach (var (x, y) in new[] { (20.0, Dun), (25.0, Dun), (36.5, Tun), (39.5, Tun) })
            k.Prop("Tunnel Timber Frame", x, 21.1, y, 90);
        SProp("Cider Barrel", 23.0, 22.1, Dun, 0, 0.4, 0.4);
        SProp("Wooden Crate", 35.4, 22.0, Tun, 0, 0.5, 0.5);
        Salvage(27.5, Dun, 20.4);
        GroundSpawner("Smugglers' Tunnel", 38.0, Tun, 21.1);
        k.Prop("Wall Torch", 28.0, 19.6, Dun + 1.8, 0);
        k.Prop("Wall Torch", 40.0, 19.6, Tun + 1.8, 0);
        for (var x = 17.5; x <= 29.0; x += 2.3)
            k.Waypoint(x, 21.1, Dun);
        for (var x = 34.6; x <= 42.6; x += 2.0)
            k.Waypoint(x, 21.1, Tun);
    }

    // ==== Outside the precinct: the paupers' field ======================================================================

    void PaupersField()
    {
        // Graves, dead trees and lamps round the outside, clear of the windows' approaches and of the tower's north face
        // (the climb's foot, x -36..-22, z -34..-26).
        var rng = new Rng(19);
        for (var i = 0; i < 150; i++)
        {
            var x = -60 + rng.Next() * 120;
            var z = -54 + rng.Next() * 30;
            if (z > -20 || (x > -38 && x < -20 && z > -36) || (x > 1 && x < 19 && z > -32) || Math.Abs(Math.Abs(x) - 58.7) < 3 && z > -36 || x * x + (z + 45) * (z + 45) < 36)
                continue; // (clear of the climb's foot, the transept's windows and the team spawns and flags at the sides)
            var name = rng.Next() < 0.6 ? "Gothic Headstone" : "Churchyard Headstone";
            if (!NearKeep(x, G, z, 1.6))
                SProp(name, x, z, G, rng.Next() * 30 - 15, 0.4, 0.2);
        }
        foreach (var (x, z) in new[] { (-50.0, -40.0), (-14.0, -46.0), (24.0, -40.0), (44.0, -48.0), (58.0, -30.0), (58.0, 20.0), (-61.5, 30.0), (-58.0, 40.0), (0.0, 50.0), (40.0, 50.0) })
        {
            k.Prop("Gnarled Dead Tree", x, z);
            Solid(x - 0.3, z - 0.3, x + 0.3, z + 0.3, G, "tree");
        }
        foreach (var (x, z) in new[] { (-40.0, -30.0), (-18.0, -30.0), (10.0, -36.0), (30.0, -22.0), (-58.0, -20.0), (59.0, -4.0), (-20.0, 50.0), (30.0, 50.0) })
            k.Prop("Lantern Street Lamp", x, z);
        k.DecalScatter(["Mud Patch", "Leaf Litter", "Puddle", "Dry Grass", "Gib Smear"], -63, -55, 63, -17, 70, 171, gap: 2.0);
        k.DecalScatter(["Mud Patch", "Leaf Litter", "Puddle"], -63, 45, 63, 55, 24, 172, gap: 2.0);
        k.WaypointGrid(-63, -55, 63, -17, G, 4.0);
        k.WaypointGrid(-63, -16, -55, 55, G, 4.0);
        k.WaypointGrid(51, -16, 63, 55, G, 4.0);
        k.WaypointGrid(-54, 45, 51, 55, G, 4.0);
    }

    /// <summary>More dressing, mostly on the walls and overhead (torches, glass, banners, chandeliers, gargoyles, bones), out of everyone's way.</summary>
    void Dressing()
    {
        // Torches along the walls of the transept, choir, lady chapel, vestry, chapter house, barn and lodge (back to the wall).
        foreach (var z in new[] { -24.0, -12.0, -1.0 })
        {
            k.Prop("Wall Torch", 4.6, z, 2.7, 90);
            k.Prop("Wall Torch", 15.4, z, 2.7, -90);
        }
        foreach (var x in new[] { 19.0, 25.0, 29.0, 35.0 })
        {
            k.Prop("Wall Torch", x, -15.4, 2.7, 0);
            k.Prop("Wall Torch", x, 3.4, 2.7, 180);
        }
        foreach (var x in new[] { 40.0, 47.0 })
        {
            k.Prop("Wall Torch", x, -15.4, 2.7, 0);
            k.Prop("Wall Torch", x, 3.4, 2.7, 180);
        }
        foreach (var x in new[] { 18.0, 24.0, 30.0, 36.0 })
            k.Prop("Wall Torch", x, 13.6, 2.6, 180);
        foreach (var z in new[] { 6.0, 11.0, 19.0 })
            k.Prop("Wall Torch", 15.6, z, 2.7, -90);
        foreach (var x in new[] { -30.0, -18.0, -6.0, 6.0 })
            k.Prop("Wall Torch", x, 43.6, 2.6, 180);
        k.Prop("Wall Torch", -54.0, 20, 2.4, 90);
        // Stained glass high in the cathedral's outer walls (inside faces), and banners.
        foreach (var x in new[] { -20.0, -12.0, -4.0 })
            k.Prop(x == -12.0 ? "Lancet Window Red" : "Lancet Window Blue", x, -15.4, Gal + 0.6, 0);
        foreach (var x in new[] { 20.0, 34.0 })
            k.Prop("Lancet Window Red", x, -15.4, 6.0, 0);
        foreach (var z in new[] { -22.0 })
        {
            k.Prop("Lancet Window Blue", 4.6, z, 5.0, 90);
            k.Prop("Lancet Window Blue", 15.4, z, 5.0, -90);
        }
        k.Prop("Lancet Window Red", 44.0, -15.4, 5.0, 0);
        foreach (var (x, z, yaw) in new[] { (6.0, 3.4, 180.0), (14.0, 3.4, 180.0), (37.4, -10.0, -90.0), (16.0, 6.0, 90.0), (-33.6, 9.0, 90.0) })
            k.Prop("Hanging Tapestry", x, z, 1.2, yaw);
        // Chandeliers over the galleries and the crossing; lanterns in the cloister walks and the barn.
        foreach (var x in new[] { -28.0, -16.0, -4.0 })
        {
            k.Prop("Iron Chandelier", x, -13.0, Gal + 1.4);
            k.Prop("Iron Chandelier", x, 1.0, Gal + 1.4);
        }
        foreach (var z in new[] { 12.0, 20.0 })
        {
            k.Prop("Hanging Lantern", -33.0, z, 2.0);
            k.Prop("Hanging Lantern", 3.0, z, 2.0, 180);
        }
        // Gargoyles round the roofs and the precinct gate piers.
        foreach (var (x, z, y, yaw) in new[] { (4.3, -28.3, 18.6, 180.0), (15.7, -28.3, 18.6, 180.0), (17.0, -16.3, 16.6, 180.0), (37.0, -16.3, 16.6, 180.0), (49.7, -15.7, 12.6, 90.0), (49.7, 3.7, 12.6, 90.0), (-34.3, 3.7, 18.0, -90.0), (-34.3, -15.7, 18.0, -90.0) })
            k.Prop("Stone Gargoyle", x, z, y, yaw);
        // Bones and coffins below.
        foreach (var (x, z) in new[] { (18.5, -8.0), (36.0, -14.0), (31.0, 0.0) })
            k.Prop("Skull Heap", x, z, Crypt);
        foreach (var (x, z) in new[] { (-24.0, 8.5), (-18.5, 23.5), (-7.5, 12.5), (13.5, 7.5) })
            k.Prop("Skull Heap", x, z, Dun);
        foreach (var (x, z) in new[] { (-24.5, 7.0), (-16.5, 24.8) })
            SProp("Plain Coffin", x, z, Dun, 0, 1.0, 0.33);
        foreach (var (x, z) in new[] { (12.6, -15.0), (6.0, -14.9) })
            k.Prop("Skull Heap", x, z, Crypt);
        // The barn's stalls and hay, the lodge's crates, the close's railings and graves.
        foreach (var (x, z) in new[] { (-29.5, 33.0), (-24.0, 33.5), (-12.0, 32.0), (3.0, 32.0) })
            SProp("Hay Bale", x, z, G, 0, 0.6, 0.4);
        SProp("Cider Barrel", -13.0, 42.8, G, 0, 0.4, 0.4);
        SProp("Wooden Crate", -2.5, 42.6, G, 0, 0.5, 0.5);
        SProp("Wooden Crate", -44.0, 23.8, G, 0, 0.5, 0.5);
        foreach (var x in new[] { -51.0, -47.0 })
            SProp("Churchyard Railings", x, 30.5, G, 0, 1.6, 0.1);
        foreach (var (x, z) in new[] { (-50.0, 38.0), (-46.0, 41.5), (-36.5, 40.0) })
            SProp("Gothic Headstone", x, z, G, 180, 0.4, 0.2);
        // The graveyard's plot railings.
        foreach (var (x, z, yaw) in new[] { (22.0, 15.2, 0.0), (36.0, 15.2, 0.0), (42.0, 15.2, 0.0), (46.5, 30.0, 90.0), (25.0, 25.0, 0.0) })
            SProp("Churchyard Railings", x, z, G, yaw, 1.6, 0.1);
    }

    bool NearKeep(double x, double y, double z, double r) => _keep.Any(q => Math.Abs(q.Y - y) < 1.5 && (q.X - x) * (q.X - x) + (q.Z - z) * (q.Z - z) < (q.R + r) * (q.R + r));

    // ==== The bell: a siege up the tower (map logic) =====================================================================

    void BellLogic()
    {
        var l = k.Logic;
        l.Entity("zclimb", "Climb", ("box", B6(-33.6, G, -30.0, -24.4, 2.5, -26.02)), ("top", P(-29.0, Belfry, -23.6)), ("over", 1.6));
        l.Zone("Belfry", -33, Belfry - 0.1 + O, -25, -25, Belfry + 3 + O, -17);
        l.Button("Bell", -28.3, Belfry + 1.3 + O, -20.25, yaw: 180, prompt: "ring the bell (the horde climbs the tower: hold the belfry)", cooldown: 240);
        l.Challenge("Siege", 75, zone: "Belfry");
        l.Sound("BellToll", "mortarWhistle", -29, Belfry + 2 + O, -21, volume: 1.4, range: 0);
        l.Text("SiegeText", "The bell tolls! The horde is climbing the tower: hold the belfry.");
        l.Text("SiegeWon", "The siege is broken. The belfry held.");
        l.Text("SiegeLost", "The belfry has fallen.");
        l.Text("Breach", "They're over the parapet!");
        l.Wire("Bell.pressed", "Climb.start")
            .Wire("Climb.started", "Siege.start").Wire("Climb.started", "BellToll.play").Wire("Climb.started", "SiegeText.show")
            .Wire("Climb.reached", "Breach.show")
            .Wire("Siege.survived", "Climb.giveAll").Wire("Siege.survived", "SiegeWon.show").Wire("Siege.failed", "SiegeLost.show")
            .Wire("Siege.ended", "Climb.stop");
    }

    // ==== Players' spawns ==============================================================================================

    void Spawns()
    {
        // The players' spawns in the nave (the start area: Zombies puts everyone here), as plain spawns.
        foreach (var (x, z) in new[] { (-29.0, -8.5), (-29.0, -3.5), (-26.5, -8.5), (-26.5, -3.5), (-29.0, -13.0), (-30.5, -0.3), (-26.5, -13.0), (-24.0, -0.3) })
            k.Op(("op", "Spawn"), ("x", Kit.R(x)), ("z", Kit.R(z)));
        k.Waypoint(-28, -6);
        // For the other modes on a Zombies map (every door open: one big map), team spawns, a hill and flags out in the
        // paupers' field round the precinct: Blue west, Red east, the hill north, each flag near its team.
        foreach (var z in new[] { -6.0, 0.0, 6.0, 12.0 })
        {
            k.Spawn(0, -59.0, z);
            k.Spawn(1, 58.5, z);
        }
        k.Hill(0.0, -45.0, 4.0);
        k.Flag(-59.0, -32.0);
        k.Flag(58.5, -32.0);
    }

    // ==== The layout check ===============================================================================================

    void CheckLayout()
    {
        bool Same(double a, double b) => Math.Abs(a - b) < 1.5;
        bool Overlap((double X0, double Z0, double X1, double Z1) a, (double X0, double Z0, double X1, double Z1) b) => a.X0 < b.X1 && a.X1 > b.X0 && a.Z0 < b.Z1 && a.Z1 > b.Z0;
        bool Hits((double X0, double Z0, double X1, double Z1) r, double x, double z, double rad)
        {
            var cx = Math.Clamp(x, r.X0, r.X1);
            var cz = Math.Clamp(z, r.Z0, r.Z1);
            return (cx - x) * (cx - x) + (cz - z) * (cz - z) < rad * rad;
        }
        string? AreaOf(double x, double y, double z)
        {
            foreach (var (name, boxes) in _areas)
                foreach (var b in boxes)
                    if (x >= b[0] && x <= b[2] && z >= b[1] && z <= b[3] && y >= b[4] && y <= b[5])
                        return name;
            return null;
        }
        for (var i = 0; i < _machines.Count; i++)
        {
            var m = _machines[i];
            var r = (m.X0, m.Z0, m.X1, m.Z1);
            var cx = (m.X0 + m.X1) / 2;
            var cz = (m.Z0 + m.Z1) / 2;
            if (AreaOf(cx, m.Y + 0.1, cz) is not { } a)
                _problems.Add($"{m.What} is in no area");
            else if (AreaOf(m.X0 + 0.02, m.Y + 0.1, m.Z0 + 0.02) != a || AreaOf(m.X1 - 0.02, m.Y + 0.1, m.Z1 - 0.02) != a)
                _problems.Add($"{m.What} pokes out of {a}");
            for (var j = i + 1; j < _machines.Count; j++)
                if (Same(m.Y, _machines[j].Y) && Overlap(r, (_machines[j].X0, _machines[j].Z0, _machines[j].X1, _machines[j].Z1)))
                    _problems.Add($"{m.What} overlaps {_machines[j].What}");
            foreach (var q in _keep)
                if (Same(m.Y, q.Y) && !(q.What.EndsWith("front") && Math.Abs(q.X - cx) < 2.2 && Math.Abs(q.Z - cz) < 2.2 && m.What.StartsWith(q.What.Replace(" front", ""))) && Hits(r, q.X, q.Z, q.R - 0.2))
                    _problems.Add($"{m.What} is in the way of {q.What} ({q.X:0.##}, {q.Z:0.##})");
            foreach (var s in _solids)
                if (Same(m.Y, s.Y) && Overlap(r, (s.X0, s.Z0, s.X1, s.Z1)))
                    _problems.Add($"{m.What} overlaps the {s.What}");
        }
        foreach (var s in _solids)
            foreach (var q in _keep)
                if (Same(s.Y, q.Y) && Hits((s.X0, s.Z0, s.X1, s.Z1), q.X, q.Z, q.R - 0.1))
                    _problems.Add($"{s.What} at ({(s.X0 + s.X1) / 2:0.##}, {(s.Z0 + s.Z1) / 2:0.##}) is in the way of {q.What} ({q.X:0.##}, {q.Z:0.##})");
        for (var i = 0; i < _wallBuyList.Count; i++)
        {
            var wb = _wallBuyList[i];
            var (fx, fz) = Fwd(wb.Yaw);
            var r = Math.Abs(fx) < 0.5
                ? (wb.X - 0.7, Math.Min(wb.Z - fz * 0.05, wb.Z + fz * 0.4), wb.X + 0.7, Math.Max(wb.Z - fz * 0.05, wb.Z + fz * 0.4))
                : (Math.Min(wb.X - fx * 0.05, wb.X + fx * 0.4), wb.Z - 0.7, Math.Max(wb.X - fx * 0.05, wb.X + fx * 0.4), wb.Z + 0.7);
            var stand = AreaOf(wb.X + fx * 0.8, wb.Y + 0.1, wb.Z + fz * 0.8);
            if (stand == null || stand != AreaOf(wb.X + fx * 0.1, wb.Y + 0.1, wb.Z + fz * 0.1))
                _problems.Add($"wall buy {wb.Build} at ({wb.X}, {wb.Z}): its front isn't inside one area");
            foreach (var h in _holes)
                if (Same(wb.Y, h.Y) && Overlap(r, (h.X0, h.Z0, h.X1, h.Z1)))
                    _problems.Add($"wall buy {wb.Build} at ({wb.X}, {wb.Z}) is over {h.What}");
            foreach (var m in _machines)
                if (Same(wb.Y, m.Y) && Overlap(r, (m.X0, m.Z0, m.X1, m.Z1)))
                    _problems.Add($"wall buy {wb.Build} at ({wb.X}, {wb.Z}) is behind the {m.What}");
            foreach (var s in _solids)
                if (Same(wb.Y, s.Y) && Overlap(r, (s.X0, s.Z0, s.X1, s.Z1)))
                    _problems.Add($"wall buy {wb.Build} at ({wb.X}, {wb.Z}) is behind the {s.What}");
            for (var j = i + 1; j < _wallBuyList.Count; j++)
                if (Same(wb.Y, _wallBuyList[j].Y) && Math.Abs(wb.X - _wallBuyList[j].X) < 1.6 && Math.Abs(wb.Z - _wallBuyList[j].Z) < 1.6)
                    _problems.Add($"wall buy {wb.Build} at ({wb.X}, {wb.Z}) overlaps {_wallBuyList[j].Build}");
        }
        // Every part category has its machine (ZombiesMap.SlotCategories), and every perk one.
        var cats = _slots.Items.OfType<JObject>().Select(o => o.Str("category") ?? "").ToList();
        foreach (var c in ZombiesMap.SlotCategories)
        {
            var want = c == ZombiesMap.SlotCategoryGrenade ? "grenade" : ((PartCategory)c).ToString();
            if (!cats.Contains(want, StringComparer.OrdinalIgnoreCase))
                _problems.Add($"no slot machine for {want}");
        }
    }

    // ==== The "zombies" section ===========================================================================================

    JObject ZombiesSection()
    {
        var areas = new JArray();
        foreach (var (name, boxes) in _areas)
        {
            var b0 = boxes[0];
            var o = new JObject(("name", name), ("rect", JArray.Of(Kit.R(b0[0]), Kit.R(b0[1]), Kit.R(b0[2]), Kit.R(b0[3]))), ("y", JArray.Of(Kit.R(b0[4] + O), Kit.R(b0[5] + O))));
            if (boxes.Count > 1)
                o.Add("more", new JArray(boxes.Skip(1).Select(b => (JNode)JArray.Of(Kit.R(b[0]), Kit.R(b[1]), Kit.R(b[2]), Kit.R(b[3]), Kit.R(b[4] + O), Kit.R(b[5] + O)))));
            areas.Add(o);
        }
        return new JObject(
            ("start", "Nave"),
            ("machineProps", true),
            ("areas", areas),
            ("doors", _doors),
            ("windows", _windows),
            ("spawners", _spawners),
            ("wallBuys", _wallBuys),
            ("box", new JObject(("cost", 950), ("start", 0), ("spots", _box))),
            ("workbenches", _benches),
            ("perks", _perks),
            ("slotUpgrade", _slotUp),
            ("power", _power),
            ("salvage", _salvage),
            ("slots", _slots),
            ("casino", _casino));
    }
}
